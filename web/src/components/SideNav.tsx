import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Search, LogOut } from 'lucide-react';
import { auth } from '../api';
import { useMe } from '../queries';
import { iniciais } from '../lib';
import { NAV_DOMAINS, domainForPath, itemForPath, type NavDomain, type NavItem } from '../nav.config';
import { canAny } from '../permissions';

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

  // Renderização por cargo: esconde domínios/itens cujas permissões o usuário não possui.
  // `requires` ausente = visível para todos (canAny com lista vazia retorna true). Admin faz bypass.
  const domainVisible = (d: NavDomain) => canAny(me, d.requires ?? []);
  const itemVisible = (it: NavItem) => canAny(me, it.requires ?? []);

  const go = (to?: string) => {
    if (!to) return;
    nav(to);
    setQuery('');
    onClose();
  };

  // Selecting a domain shows its panel; a directly-routable domain (home) also navigates.
  const selectDomain = (d: NavDomain) => {
    setSelectedKey(d.key);
    if (d.to) go(d.to);
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
      if (!domainVisible(domain)) continue;
      for (const group of domain.groups) {
        for (const item of group.items) {
          if (item.to && itemVisible(item) && norm(t(item.labelKey)).includes(q)) out.push({ domain, item });
        }
      }
    }
    return out;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query, t, me]);

  const topDomains = NAV_DOMAINS.filter((d) => !d.footer && domainVisible(d));
  const footerDomains = NAV_DOMAINS.filter((d) => d.footer && domainVisible(d));

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
              onClick={() => selectDomain(d)}
            />
          ))}
          <div className='nav-rail-spacer' />
          {footerDomains.map((d) => (
            <RailButton
              key={d.key}
              icon={d.icon}
              label={t(d.labelKey)}
              active={d.key === selected.key}
              onClick={() => selectDomain(d)}
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
            <DomainPanel selected={selected} activeTo={activeTo} onGo={go} />
          )}
        </div>
      </aside>
    </>
  );
}

/**
 * The panel body for the selected domain. An `index` domain (home) renders an
 * index of every routable page grouped by domain instead of its own groups;
 * any other domain renders its title + groups as before.
 */
function DomainPanel({
  selected,
  activeTo,
  onGo,
}: {
  selected: NavDomain;
  activeTo: string | null;
  onGo: (to?: string) => void;
}) {
  const { t } = useTranslation();
  const me = useMe().data ?? null;
  const domainVisible = (d: NavDomain) => canAny(me, d.requires ?? []);
  const itemVisible = (it: NavItem) => canAny(me, it.requires ?? []);

  if (selected.index) {
    return (
      <div className='nav-panel-body'>
        {NAV_DOMAINS.filter((d) => !d.footer && !d.index && domainVisible(d)).map((d) => {
          const items = d.groups.flatMap((g) => g.items).filter((it) => it.to && !it.soon && itemVisible(it));
          if (items.length === 0) return null;
          return (
            <div className='nav-group' key={d.key}>
              <div className='nav-subgroup'>{t(d.labelKey)}</div>
              {items.map((item) => (
                <PanelItem
                  key={item.to}
                  icon={item.icon}
                  label={t(item.labelKey)}
                  active={item.to === activeTo}
                  onClick={() => onGo(item.to)}
                />
              ))}
            </div>
          );
        })}
      </div>
    );
  }

  return (
    <div className='nav-panel-body'>
      <div className='nav-panel-title'>{t(selected.titleKey ?? selected.labelKey)}</div>
      {selected.groups.map((group, gi) => (
        <div className='nav-group' key={group.labelKey ?? gi}>
          {group.labelKey && <div className='nav-subgroup'>{t(group.labelKey)}</div>}
          {group.items.filter(itemVisible).map((item) => (
            <PanelItem
              key={item.labelKey}
              icon={item.icon}
              label={t(item.labelKey)}
              active={!!item.to && item.to === activeTo}
              soon={item.soon}
              onClick={() => onGo(item.to)}
            />
          ))}
        </div>
      ))}
    </div>
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
