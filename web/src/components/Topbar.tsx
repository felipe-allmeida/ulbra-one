import { type ReactNode, useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Menu, Bell, Sun, Moon, Building2, ChevronDown } from 'lucide-react';
import { useApp, ALL_COMPANIES } from '../store';
import { useMe, useRoles, useUnreadNotificationsCount } from '../queries';
import { useImpersonation } from '../impersonation';
import { useTheme } from '../theme';
import { roleLabel } from '../permissions';

// Top bar: breadcrumb + company selector + "Ver como" (admin only) + notifications + theme toggle.
export function Topbar({ crumb, onOpenNav }: { crumb: ReactNode; onOpenNav: () => void }) {
  return (
    <header className='topbar'>
      <div className='topbar-menu'>
        <button className='icon-btn hamburger' onClick={onOpenNav} aria-label='Menu'>
          <Menu />
        </button>
        <div className='crumb'>{crumb}</div>
      </div>
      <div className='topbar-actions'>
        <EmpresaSelector />
        <VerComo />
        <NotificationBell />
        <ThemeToggle />
      </div>
    </header>
  );
}

// Company selector — drives the shared `empresaId` (persisted). Was previously per-page.
function EmpresaSelector() {
  const { t } = useTranslation();
  const { empresas, empresa, empresaId, isAllCompanies, canSeeAllCompanies, setEmpresaId } = useApp();
  if (empresas.length === 0) return null;
  // The transparent <select> overlays the whole chip (see .emp-select in index.css), so a click
  // anywhere — icon, name or chevron — opens the native dropdown. The visible parts are inert.
  const label = isAllCompanies ? t('topbar.allCompanies') : (empresa?.legalName ?? t('topbar.company'));
  return (
    <div className='emp'>
      <Building2 className='emp-icon' aria-hidden />
      <span className='emp-name'>{label}</span>
      <ChevronDown className='emp-chevron' aria-hidden />
      <select
        className='emp-select'
        aria-label={t('topbar.company')}
        value={empresaId}
        onChange={(e) => setEmpresaId(e.target.value)}
      >
        {/* Consolidated roles (AEL-906) can pick "Todas as empresas" to see every company at once. */}
        {canSeeAllCompanies && <option value={ALL_COMPANIES}>{t('topbar.allCompanies')}</option>}
        {empresas.map((e) => (
          <option key={e.id} value={e.id}>
            {e.legalName}
          </option>
        ))}
      </select>
    </div>
  );
}

// "Ver como" — admin-only role impersonation (client-side render only; see impersonation.tsx).
// Visibility keys off the REAL admin flag (useMe), not usePermissions — the latter turns false
// while impersonating, which would hide the selector and trap the admin in the picked role.
function VerComo() {
  const { t } = useTranslation();
  const realIsAdmin = useMe().data?.isAdmin ?? false;
  const { asRole, setAsRole } = useImpersonation();
  const roles = useRoles({ enabled: realIsAdmin });
  if (!realIsAdmin) return null;
  const options = (roles.data ?? []).filter((r) => r.name !== 'Admin');
  return (
    <label className='verocomo'>
      <span className='verocomo-label'>{t('topbar.viewAs')}</span>
      <select aria-label={t('topbar.viewAs')} value={asRole ?? ''} onChange={(e) => setAsRole(e.target.value || null)}>
        <option value=''>{t('topbar.myRole')}</option>
        {options.map((r) => (
          <option key={r.id} value={r.name}>
            {roleLabel(r.name)}
          </option>
        ))}
      </select>
    </label>
  );
}

// Bell + unread dot in the topbar — opens the inbox. Count polls every 30s
// (see useUnreadNotificationsCount). The exact count stays in the aria-label/title.
function NotificationBell() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data } = useUnreadNotificationsCount();
  const count = data?.count ?? 0;

  // When the unread count rises, a workflow item of the user's moved (e.g. a requisition returned by Finance,
  // then re-approved). Refresh the workflow lists/details so the status updates without a manual reload — the
  // 30s bell poll is the cross-session signal (AEL-935). Baseline on the first real value to avoid firing on load.
  const prevCount = useRef<number | null>(null);
  useEffect(() => {
    if (data === undefined) return;
    if (prevCount.current !== null && count > prevCount.current) {
      for (const key of [
        ['requisicoes'],
        ['requisicao'],
        ['requisitionStageCounts'],
        ['myDecisions'],
        ['myTriageDecisions'],
        ['payables'],
      ])
        void qc.invalidateQueries({ queryKey: key });
    }
    prevCount.current = count;
  }, [data, count, qc]);

  const label = count > 0 ? t('notifications.unreadBadgeAria', { count }) : t('nav.notifications');
  return (
    <Link to='/app/notificacoes' className='icon-btn notif' aria-label={label} title={label}>
      <Bell />
      {count > 0 && <span className='notif-dot' aria-hidden />}
    </Link>
  );
}

function ThemeToggle() {
  const { t } = useTranslation();
  const { theme, toggle } = useTheme();
  return (
    <button className='icon-btn' onClick={toggle} aria-label={t('topbar.themeToggle')} title={t('topbar.themeToggle')}>
      {theme === 'dark' ? <Sun /> : <Moon />}
    </button>
  );
}
