import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Search, LogOut } from 'lucide-react';
import { auth } from '../api';
import { useMe } from '../queries';
import { iniciais } from '../lib';
import { NAV_DOMAINS, domainForPath, itemForPath, type NavDomain, type NavItem } from '../nav.config';

const norm = (s: string) =>
  s
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .trim();

/**
 * Primary navigation: a domain rail + a contextual panel for the selected
 * domain. On mobile the whole thing slides in as an off-canvas drawer.
 */
export function SideNav({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { pathname } = useLocation();
  const me = useMe().data ?? null;

  const [selectedKey, setSelectedKey] = useState(() => domainForPath(pathname).key);
  const [query, setQuery] = useState('');

  // Follow route changes so the rail highlights the domain of the current screen.
  useEffect(() => {
    setSelectedKey(domainForPath(pathname).key);
  }, [pathname]);

  const selected = NAV_DOMAINS.find((d) => d.key === selectedKey) ?? NAV_DOMAINS[0];
  const activeTo = itemForPath(pathname)?.to ?? null;

  const go = (to?: string) => {
    if (!to) return;
    nav(to);
    setQuery('');
    onClose();
  };

  const logout = () => {
    auth.clear();
    nav('/');
  };

  const results = useMemo(() => {
    const q = norm(query);
    if (!q) return null;
    const out: { domain: NavDomain; item: NavItem }[] = [];
    for (const domain of NAV_DOMAINS) {
      for (const group of domain.groups) {
        for (const item of group.items) {
          if (item.to && norm(t(item.labelKey)).includes(q)) out.push({ domain, item });
        }
      }
    }
    return out;
  }, [query, t]);

  const topDomains = NAV_DOMAINS.filter((d) => !d.footer);
  const footerDomains = NAV_DOMAINS.filter((d) => d.footer);

  return (
    <>
      {open && <div className='sb-backdrop show' onClick={onClose} />}
      <aside className={`side-nav ${open ? 'open' : ''}`}>
        <nav className='nav-rail' aria-label={t('nav.sections.finance')}>
          <div
            className='nav-rail-avatar'
            title={me ? `${me.name ?? ''}${me.role ? ` · ${me.role}` : ''}` : t('common.appName')}
          >
            {me ? iniciais(me.name ?? '') : 'U1'}
          </div>
          {topDomains.map((d) => (
            <RailButton
              key={d.key}
              icon={d.icon}
              label={t(d.labelKey)}
              active={d.key === selected.key}
              onClick={() => setSelectedKey(d.key)}
            />
          ))}
          <div className='nav-rail-spacer' />
          {footerDomains.map((d) => (
            <RailButton
              key={d.key}
              icon={d.icon}
              label={t(d.labelKey)}
              active={d.key === selected.key}
              onClick={() => setSelectedKey(d.key)}
            />
          ))}
          <RailButton icon={<LogOut />} label={t('nav.logout')} onClick={logout} />
        </nav>

        <div className='nav-panel'>
          <div className='nav-panel-search'>
            <Search />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={t('nav.search')}
              aria-label={t('nav.search')}
            />
          </div>

          {results ? (
            <div className='nav-panel-body'>
              {results.length === 0 ? (
                <p className='nav-panel-empty'>{t('nav.noResults')}</p>
              ) : (
                results.map(({ domain, item }) => (
                  <PanelItem
                    key={item.to}
                    icon={item.icon}
                    label={t(item.labelKey)}
                    hint={t(domain.labelKey)}
                    active={item.to === activeTo}
                    onClick={() => go(item.to)}
                  />
                ))
              )}
            </div>
          ) : (
            <div className='nav-panel-body'>
              <div className='nav-panel-title'>{t(selected.titleKey ?? selected.labelKey)}</div>
              {selected.groups.map((group, gi) => (
                <div className='nav-group' key={group.labelKey ?? gi}>
                  {group.labelKey && <div className='nav-subgroup'>{t(group.labelKey)}</div>}
                  {group.items.map((item) => (
                    <PanelItem
                      key={item.labelKey}
                      icon={item.icon}
                      label={t(item.labelKey)}
                      active={!!item.to && item.to === activeTo}
                      soon={item.soon}
                      onClick={() => go(item.to)}
                    />
                  ))}
                </div>
              ))}
            </div>
          )}
        </div>
      </aside>
    </>
  );
}

function RailButton({
  icon,
  label,
  active,
  onClick,
}: {
  icon: ReactNode;
  label: string;
  active?: boolean;
  onClick: () => void;
}) {
  return (
    <button className={`nav-rail-btn ${active ? 'active' : ''}`} onClick={onClick} title={label}>
      {icon}
      <span>{label}</span>
    </button>
  );
}

function PanelItem({
  icon,
  label,
  hint,
  active,
  soon,
  onClick,
}: {
  icon: ReactNode;
  label: string;
  hint?: string;
  active?: boolean;
  soon?: boolean;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  return (
    <button
      className={`nav-panel-item ${active ? 'active' : ''} ${soon ? 'soon' : ''}`}
      disabled={soon}
      onClick={onClick}
    >
      {icon}
      <span className='nav-panel-item-text'>
        <span className='nav-panel-item-label'>{label}</span>
        {hint && <span className='nav-panel-item-hint'>{hint}</span>}
      </span>
      {soon && <span className='soon-badge'>{t('common.soon')}</span>}
    </button>
  );
}
