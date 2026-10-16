import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useLocation, Navigate } from 'react-router-dom';
import { ArrowRight } from 'lucide-react';
import { api, auth } from '../api';
import { GoogleSignInButton } from './google-signin';
import { getEnvironment } from '../config/env';
import { getErrorMessage } from '../apiError';

/**
 * Local test accounts (one per role) seeded by the backend in Development
 * (`DevDataSeeder.SeedLocalUsersAsync`). The buttons only appear when the environment
 * is `development` (local or the dev container on Azure), where `/auth/dev-token`
 * is enabled. Otherwise, only the Google login is shown.
 */
const DEV_ACCOUNTS = [
  { label: 'Entrar como Admin', email: 'admin@ulbra.local', name: 'Admin (dev)' },
  { label: 'Entrar como Analista Financeiro', email: 'financeiro@ulbra.local', name: 'Financeiro (dev)' },
  { label: 'Entrar como Controladoria', email: 'controladoria@ulbra.local', name: 'Controladoria (dev)' },
  { label: 'Entrar como Aprovador', email: 'aprovador@ulbra.local', name: 'Aprovador (dev)' },
  { label: 'Entrar como Analista Contábil', email: 'contabil@ulbra.local', name: 'Contábil (dev)' },
  { label: 'Entrar como Solicitante', email: 'solicitante@ulbra.local', name: 'Solicitante (dev)' },
];

/**
 * The app's front door — it answers on `/` (there is no landing page) and on `/login`.
 * Left side signs the user in; right side is the brand panel (hidden under 760px).
 */
export function LoginPage() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? '/app';
  const isDev = getEnvironment() === 'development';

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Since `/` is the login, a user with a live session would otherwise be asked to sign in
  // again just for opening the root. Validity, not mere presence: a stale token stays here.
  if (auth.hasValidToken()) return <Navigate to={from} replace />;

  async function quickLogin(email: string, name: string) {
    setBusy(true);
    setError(null);
    try {
      const res = await api.devLogin(email, name);
      auth.set(res.token);
      nav(from, { replace: true });
    } catch {
      setError(t('login.error'));
      setBusy(false);
    }
  }

  async function onGoogleCredential(idToken: string) {
    setBusy(true);
    setError(null);
    try {
      const res = await api.googleSignIn(idToken);
      auth.set(res.token);
      nav(from, { replace: true });
    } catch (e) {
      // Sign-in is invitation-only, so "não cadastrado" is a routine outcome — say which one it was
      // (Auth.UserNotRegistered / Auth.UserInactive) instead of the generic failure line.
      setError(getErrorMessage(e));
      setBusy(false);
    }
  }

  return (
    <div className='auth'>
      {/* form */}
      <div className='auth-form-side'>
        <div className='auth-card'>
          <div className='auth-brand'>
            <span className='auth-logo'>U1</span> {t('common.appName')}
          </div>
          <h1 className='auth-title'>{t('login.title')}</h1>
          <p className='auth-sub'>{t('login.subtitle')}</p>

          <GoogleSignInButton onCredential={onGoogleCredential} />

          {isDev && (
            <>
              <div className='auth-divider'>
                <span>{t('login.orDivider')}</span>
              </div>
              <div className='auth-fields'>
                {DEV_ACCOUNTS.map((acc) => (
                  <button
                    key={acc.email}
                    type='button'
                    className='auth-submit'
                    disabled={busy}
                    onClick={() => quickLogin(acc.email, acc.name)}
                  >
                    {acc.label} <ArrowRight />
                  </button>
                ))}
              </div>
              <p className='auth-devnote'>Ambiente de desenvolvimento — login rápido por cargo.</p>
            </>
          )}

          {error && <div className='auth-error'>{error}</div>}
        </div>
      </div>

      {/* Brand panel — the half that used to be the landing page's job: say what this is and who
          builds it. The side already clips (`overflow: hidden`) and centers its child, so dropping
          a full-bleed image in here later is a matter of adding one next to `.auth-brand-content`. */}
      <div className='auth-brand-side'>
        <div className='auth-brand-blob' />
        <div className='auth-brand-content'>
          <div className='auth-brand-logo'>
            <span className='auth-logo auth-logo-lg'>U1</span> {t('common.appName')}
          </div>
          <p className='auth-brand-tag'>{t('login.brandTagline')}</p>
          <div className='auth-brand-team'>
            <span className='auth-brand-team-label'>{t('login.team.label')}</span>
            <p className='auth-brand-team-body'>{t('login.team.body')}</p>
          </div>
        </div>
      </div>
    </div>
  );
}
