import { useState, type ReactNode } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { AlertTriangle, KeyRound, LogOut, RefreshCw } from 'lucide-react';
import { auth } from './api';
import { P, usePermissions, type Permission } from './permissions';

/**
 * Role-based home (v1, by redirect): on `/app` it sends each role straight to its working screen
 * instead of the generic dashboard. Roles are distinguished by their signature permissions.
 */
export function RoleLanding() {
  const { me, isAdmin, controller, finance, can, canAny } = usePermissions();
  if (!me) return null; // /auth/me still loading
  // Controllership (triage) comes BEFORE financial approval: it also has payables/treasury:approve, so
  // without this branch it would land on /app/pendencies instead of the triage Inbox (home per role).
  // Finance (pay + treasury:write, no approve/triage) comes BEFORE the generic payables:write rung so
  // only the Finance signature lands on the Gestão Financeira home.
  const to = isAdmin
    ? '/app/home'
    : controller
      ? '/app/requisitions/triage' // Controllership → triage Inbox
      : canAny([P.accountingWrite, P.accountingPost])
        ? '/app/accounting/review' // Accounting → unified "Revisão de Contabilidade" home
        : canAny([P.payablesApprove, P.treasuryApprove])
          ? '/app/pendencies' // financial approval (no triage) → approvals queue
          : finance
            ? '/app/treasury' // Finance → unified "Gestão Financeira" home
            : can(P.payablesTax)
              ? '/app/tax/review' // Tributário → análise tributária home
              : canAny([P.payablesWrite, P.payablesPay, P.receivablesWrite, P.treasuryWrite])
                ? '/app/payables' // other finance-adjacent write roles → accounts payable
                : can(P.requisitionsApprove)
                  ? '/app/requisitions/approval' // Approver → approval queue
                  : can(P.requisitionsWrite)
                    ? '/app/requisitions/mine' // Requester → "my requisitions" dashboard
                    : '/app/home';
  return <Navigate to={to} replace />;
}

/**
 * The final approver (Approver) works only in their 3 screens — routes that "leak" through
 * their read permissions (registry/structure/projects/list) redirect to the Inbox (AEL-859).
 * Passthrough for any other role; the real security stays on the server.
 */
export function FinalApproverRedirect({ children }: { children: ReactNode }) {
  const { me, finalApprover } = usePermissions();
  if (!me) return null; // /auth/me still loading
  if (finalApprover) return <Navigate to='/app/requisitions/approval' replace />;
  return <>{children}</>;
}

/**
 * Per-permission route guard — defense in depth (the real security is the server).
 * If the user has none of the permissions, redirect to the home instead of
 * rendering a data-less screen. Admin passes via bypass (see `usePermissions`).
 */
export function RequirePermission({ anyOf, children }: { anyOf: Permission[]; children: ReactNode }) {
  const { me, canAny } = usePermissions();
  if (!me) return null; // /auth/me still loading
  if (!canAny(anyOf)) return <Navigate to='/app/home' replace />;
  return <>{children}</>;
}

/**
 * An authenticated user with no role (no permissions and not admin) lands on an
 * "access being set up" screen instead of an empty app with a phantom menu and 403s everywhere.
 */
export function RoleGate({ children }: { children: ReactNode }) {
  const { me, isAdmin, isError, isPaused, refetch } = usePermissions();
  // Could not confirm the session: an error (network/5xx) OR a paused fetch (offline detected).
  // Both would leave the guard at `null` forever (blank screen) — treat as an actionable failure.
  if (isError || isPaused) {
    // A clean 401 already cleared the token and redirected in `request()` — nothing to retry, go to login.
    if (!auth.hasValidToken()) return <Navigate to='/login' replace />;
    // The token may still be valid — offer a retry instead of dropping the session.
    return <SessionErrorScreen onRetry={() => void refetch()} />;
  }
  if (!me) return null; // /auth/me still loading
  const hasCargo = isAdmin || (me.permissions?.length ?? 0) > 0;
  return hasCargo ? <>{children}</> : <NoRoleScreen email={me.email} onRecheck={() => refetch()} />;
}

/**
 * `/auth/me` failed to load due to network/server (non-401): instead of a blank screen,
 * show an actionable error. Reuses the `.error-fallback*` classes (see ErrorBoundary).
 */
function SessionErrorScreen({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const logout = () => {
    auth.clear();
    nav('/login', { replace: true });
  };
  return (
    <div className='error-fallback' role='alert'>
      <div className='error-fallback-card'>
        <span className='error-fallback-icon'>
          <AlertTriangle />
        </span>
        <h1 className='error-fallback-title'>{t('errors.session.title')}</h1>
        <p className='error-fallback-message'>{t('errors.session.message')}</p>
        <div className='error-fallback-actions'>
          <button className='btn btn-brand' onClick={onRetry}>
            {t('errors.session.retry')}
          </button>
          <button className='btn' onClick={logout}>
            {t('errors.session.logout')}
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * Authenticated user without a cargo. The cargo is assigned by an admin outside the app, so the
 * screen's job is to name the state, show WHICH account is waiting (the user may have signed in with
 * the wrong Google account) and offer a re-check — cheaper than logging out and back in once the
 * admin assigns it. Layout mirrors `.error-fallback` (fixed overlay: a plain block inside the flex
 * `#root` shrinks to content width and lands at the left edge).
 */
function NoRoleScreen({ email, onRecheck }: { email: string | null; onRecheck: () => Promise<unknown> }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const [checking, setChecking] = useState(false);
  const logout = () => {
    auth.clear();
    nav('/');
  };
  const recheck = () => {
    setChecking(true);
    void onRecheck().finally(() => setChecking(false));
  };
  return (
    <div className='no-role'>
      <div className='no-role-card'>
        <div className='no-role-head'>
          <span className='no-role-icon'>
            <KeyRound />
          </span>
          <span className='no-role-badge'>
            <i className='no-role-dot' aria-hidden='true' />
            {t('noRole.badge')}
          </span>
        </div>
        <h1 className='no-role-title'>{t('noRole.title')}</h1>
        {email && (
          <div className='no-role-account'>
            <span className='no-role-account-label'>{t('noRole.accountLabel')}</span>
            <span className='no-role-account-email'>{email}</span>
          </div>
        )}
        <p className='no-role-message'>{t('noRole.message')}</p>
        <div className='no-role-actions'>
          <button type='button' className='btn btn-brand' onClick={recheck} disabled={checking}>
            <RefreshCw className={checking ? 'no-role-spin' : undefined} />
            {checking ? t('noRole.rechecking') : t('noRole.recheck')}
          </button>
          <button type='button' className='btn' onClick={logout}>
            <LogOut />
            {t('noRole.logout')}
          </button>
        </div>
      </div>
    </div>
  );
}
