import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Search, LogOut, ChevronRight, ChevronLeft } from 'lucide-react';
import { auth } from '../api';
import { useApp } from '../store';
import { usePhone } from '../useMediaQuery';
import { useSidebarLayout, NAV_PANEL_MIN, NAV_PANEL_MAX } from '../sidebar-layout';
import { UserAvatar } from '../user-avatar';
import { useRequisitions, usePayableStageItems } from '../queries';
import {
  NAV_DOMAINS,
  domainForPath,
  domainVisibleFor,
  itemForPath,
  itemVisibleFor,
  type NavDomain,
  type NavItem,
} from '../nav.config';
import { canAny, isController, isFinalApprover, P, roleLabel, useEffectiveMe } from '../permissions';

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
  const me = useEffectiveMe();
  const phone = usePhone();

  const { collapsed, width, commitWidth } = useSidebarLayout();

  const [selectedKey, setSelectedKey] = useState(() => domainForPath(pathname).key);
  const [query, setQuery] = useState('');
  // Phone-only: which of the sheet's two levels is showing. Deliberately NOT derived from
  // `pathname` (unlike `selectedKey` below) — tying it to the route-follow effect would also
  // force the sheet back to the domain list on any in-app navigation that isn't through this
  // menu (e.g. a breadcrumb), which is not what "reset on open" is meant to do. Instead it
  // resets purely on the drawer's own open transition, so reopening the hamburger always
  // starts at the domain list, regardless of how the drawer last closed.
  const [mobileLevel, setMobileLevel] = useState<'list' | 'domain'>('list');
  useEffect(() => {
    if (open) setMobileLevel('list');
  }, [open]);

  // Follow route changes so the rail highlights the domain of the current screen.
  useEffect(() => {
    setSelectedKey(domainForPath(pathname).key);
  }, [pathname]);

  // Rendering by role: permissions + role slice (pure helpers from nav.config).
  const domainVisible = (d: NavDomain) => domainVisibleFor(me, d);
  const itemVisible = (it: NavItem) => itemVisibleFor(me, it);

  // A route outside the nav map falls back to the FIRST domain (`domainForPath`), which this role may
  // not see — the panel would render empty. Fall back to the first domain they actually see instead.
  const fromKey = NAV_DOMAINS.find((d) => d.key === selectedKey);
  const selected = fromKey && domainVisible(fromKey) ? fromKey : (NAV_DOMAINS.find(domainVisible) ?? NAV_DOMAINS[0]);
  const activeTo = itemForPath(pathname)?.to ?? null;

  // Approval-queue count badge (mock `ib`/`rdot`) — only for the final approver; no polling
  // (refetch on focus + post-decision invalidation keep the number honest).
  const { empresaId } = useApp();
  const finalApprover = isFinalApprover(me);
  const pendingQ = useRequisitions(
    { companyId: empresaId, status: 'Approval', mine: false, page: 1, pageSize: 1 },
    { enabled: finalApprover && !!empresaId },
  );
  const approvalBadge = finalApprover ? (pendingQ.data?.total ?? 0) : 0;
  // Symmetric TRIAGE-queue badge for Controllership (no real role is both, so only one of the two badges
  // is non-zero at a time on the `budget` rail).
  const controller = isController(me);
  const triageQ = useRequisitions(
    { companyId: empresaId, status: 'Triage', mine: false, page: 1, pageSize: 1 },
    { enabled: controller && !!empresaId },
  );
  const triageBadge = controller ? (triageQ.data?.total ?? 0) : 0;

  // Accounting "Precisa de você" badge (same pattern as approvalBadge): the classification review queue
  // (payables generated still without a ledger account). Only for the Accounting role (accounting:write/post
  // — Controllership sees Accounting read-only, with no pending action).
  const isAccountant = canAny(me, [P.accountingWrite, P.accountingPost]);
  const reviewPendingQ = usePayableStageItems(
    { companyId: empresaId, reviewPending: true, page: 1, pageSize: 1 },
    { enabled: isAccountant && !!empresaId },
  );
  const reviewPendingBadge = isAccountant ? (reviewPendingQ.data?.total ?? 0) : 0;

  const go = (to?: string) => {
    if (!to) return;
    nav(to);
    setQuery('');
    onClose();
  };

  // Selecting a domain shows its panel; a directly-routable domain (e.g. documentation) also navigates.
  const selectDomain = (d: NavDomain) => {
    setSelectedKey(d.key);
    if (d.to) go(d.to);
  };

  // Aggregated count-badge shown on the domain row/rail button (mock "rdot").
  const badgeFor = (d: NavDomain): number | undefined => {
    if (d.key === 'budget') return approvalBadge || triageBadge;
    if (d.key === 'accounting') return reviewPendingBadge;
    return undefined;
  };

  // Phone level-1 tap: always drills into level 2. (There used to be a direct-navigate shortcut
  // here for a domain with no items at all — the removed "Início" domain — but every remaining
  // domain has its own groups, so the sheet always has somewhere to drill into.)
  const openMobileDomain = (d: NavDomain) => {
    setSelectedKey(d.key);
    setMobileLevel('domain');
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

  // Search results (flattened across every visible domain — see `results` above) are shared
  // verbatim between the desktop panel and the phone sheet's level 1: search deliberately cuts
  // across the two-level split instead of being scoped to whichever domain is selected.
  const searchResultsBody = results && (
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
  );

  return (
    <>
      {open && <div className='sb-backdrop show' onClick={onClose} />}
      <aside
        className={`side-nav ${open ? 'open' : ''} ${phone ? 'side-nav-mobile' : ''} ${collapsed ? 'nav-collapsed' : ''}`}
        // Custom property, não `width` inline: o CSS precisa poder neutralizar a preferência abaixo
        // de 1024px (onde a sidebar é drawer e o painel tem de aparecer inteiro), e um `style`
        // inline venceria a folha. Durante o arrasto esta mesma property é escrita direto no nó,
        // sem passar pelo React — ver `onPointerDown` da alça.
        style={{ '--nav-panel-w': `${width}px` } as CSSProperties}
      >
        {phone ? (
          <nav className='mnav' aria-label={t('common.appName')}>
            <div className='mnav-header'>
              {me ? (
                <UserAvatar className='mnav-avatar' name={me.name ?? ''} pictureUrl={me.pictureUrl} />
              ) : (
                <div className='mnav-avatar'>U1</div>
              )}
              <div className='mnav-user'>
                <span className='mnav-user-name'>{me?.name ?? t('common.appName')}</span>
                {me?.role && <span className='mnav-user-role'>{roleLabel(me.role)}</span>}
              </div>
            </div>

            {mobileLevel === 'list' ? (
              <div className='mnav-list'>
                <div className='mnav-search'>
                  <Search />
                  <input
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    placeholder={t('nav.search')}
                    aria-label={t('nav.search')}
                  />
                </div>

                {searchResultsBody ?? (
                  <div className='mnav-body'>
                    {topDomains.map((d) => (
                      <MobileDomainRow
                        key={d.key}
                        domain={d}
                        label={t(d.labelKey)}
                        badge={badgeFor(d)}
                        onClick={() => openMobileDomain(d)}
                      />
                    ))}
                  </div>
                )}

                <div className='mnav-footer'>
                  {footerDomains.map((d) => (
                    <MobileDomainRow key={d.key} domain={d} label={t(d.labelKey)} onClick={() => openMobileDomain(d)} />
                  ))}
                  <button className='mnav-row mnav-footer-logout' onClick={logout}>
                    <LogOut />
                    <span className='mnav-row-label'>{t('nav.logout')}</span>
                  </button>
                </div>
              </div>
            ) : (
              <div className='mnav-domain'>
                <button className='mnav-back' onClick={() => setMobileLevel('list')} aria-label={t('common.back')}>
                  <ChevronLeft />
                  <span>{t(selected.titleKey ?? selected.labelKey)}</span>
                </button>
                <DomainPanel
                  selected={selected}
                  activeTo={activeTo}
                  onGo={go}
                  approvalBadge={approvalBadge}
                  triageBadge={triageBadge}
                  reviewPendingBadge={reviewPendingBadge}
                  hideTitle
                />
              </div>
            )}
          </nav>
        ) : (
          <>
            <nav className='nav-rail' aria-label={t('nav.sections.finance')}>
              {me ? (
                <UserAvatar
                  className='nav-rail-avatar'
                  name={me.name ?? ''}
                  pictureUrl={me.pictureUrl}
                  title={`${me.name ?? ''}${me.role ? ` · ${roleLabel(me.role)}` : ''}`}
                />
              ) : (
                <div className='nav-rail-avatar' title={t('common.appName')}>
                  U1
                </div>
              )}
              {topDomains.map((d) => (
                <RailButton
                  key={d.key}
                  icon={d.icon}
                  label={t(d.labelKey)}
                  active={d.key === selected.key}
                  badge={badgeFor(d)}
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

              {searchResultsBody ?? (
                <DomainPanel
                  selected={selected}
                  activeTo={activeTo}
                  onGo={go}
                  approvalBadge={approvalBadge}
                  triageBadge={triageBadge}
                  reviewPendingBadge={reviewPendingBadge}
                />
              )}
            </div>

            {/* A alça fica aqui, na borda do painel — é o que ela redimensiona. O gatilho de
                recolher NÃO: ele vive na topbar, à esquerda, onde Linear/Azure/Claude Desktop o
                põem, porque é chrome do app e não da sidebar (e continua alcançável quando a
                sidebar está recolhida). Ver `SidebarToggle` em Topbar.tsx. */}
            <NavResizeHandle width={width} collapsed={collapsed} onCommit={commitWidth} />
          </>
        )}
      </aside>
    </>
  );
}

const STEP = 16;

/**
 * Alça de redimensionar o painel. Um redimensionador só-mouse exclui quem navega por teclado, então
 * ela é um `separator` focável com a faixa anunciada e setas/Home/End movendo por passo.
 *
 * Durante o arrasto a largura vai direto na custom property do nó, sem passar pelo estado: um
 * `setState` por `pointermove` re-renderiza a árvore inteira do app a cada quadro, e numa tela com
 * tabela grande isso engasga. O estado (e a gravação em localStorage) só acontece no `pointerup`.
 */
function NavResizeHandle({
  width,
  collapsed,
  onCommit,
}: {
  width: number;
  collapsed: boolean;
  onCommit: (next: number) => void;
}) {
  const { t } = useTranslation();
  const ref = useRef<HTMLDivElement>(null);

  const startDrag = (e: React.PointerEvent<HTMLDivElement>) => {
    const aside = ref.current?.closest('.side-nav');
    if (!(aside instanceof HTMLElement)) return;

    const startX = e.clientX;
    const startWidth = width;
    let latest = width;

    const move = (ev: PointerEvent) => {
      // Sem clamp no mínimo aqui: o gesto precisa poder ir abaixo dele para virar "recolher" no
      // fim. O teto é travado porque passar dele não significa nada.
      latest = Math.min(startWidth + (ev.clientX - startX), NAV_PANEL_MAX);
      aside.style.setProperty('--nav-panel-w', `${Math.max(latest, NAV_PANEL_MIN)}px`);
    };
    const up = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      document.body.classList.remove('nav-resizing');
      onCommit(latest);
    };

    document.body.classList.add('nav-resizing');
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    const next = {
      ArrowLeft: width - STEP,
      ArrowRight: width + STEP,
      Home: NAV_PANEL_MIN,
      End: NAV_PANEL_MAX,
    }[e.key];
    if (next == null) return;
    e.preventDefault();
    onCommit(next);
  };

  return (
    <div
      ref={ref}
      className='nav-resize'
      role='separator'
      // Um separator com aria-valuenow É focável: este é o padrão "window splitter" do WAI-ARIA, e
      // sem tabIndex a alça vira um redimensionador só-mouse. A regra trata todo separator como
      // decorativo e não conhece a variante com valor.
      // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
      tabIndex={0}
      aria-orientation='vertical'
      aria-label={t('nav.resizeMenu')}
      aria-valuenow={collapsed ? 0 : width}
      aria-valuemin={NAV_PANEL_MIN}
      aria-valuemax={NAV_PANEL_MAX}
      onPointerDown={startDrag}
      onKeyDown={onKeyDown}
    />
  );
}

/** The panel body for the selected domain: its title + the groups this role can see. */
function DomainPanel({
  selected,
  activeTo,
  onGo,
  approvalBadge = 0,
  triageBadge = 0,
  reviewPendingBadge = 0,
  hideTitle = false,
}: {
  selected: NavDomain;
  activeTo: string | null;
  onGo: (to?: string) => void;
  /** Approval-queue count — shown on Approver's "Aprovação" item (mock `ib`). */
  approvalBadge?: number;
  /** Triage-queue count — shown on Controllership's "Triagem" item. */
  triageBadge?: number;
  /** Classification-review queue count — shown on the "Revisão de Contabilidade" item. */
  reviewPendingBadge?: number;
  /** Phone level 2 already names the domain in its back row — omits the redundant title here. */
  hideTitle?: boolean;
}) {
  const { t } = useTranslation();
  const me = useEffectiveMe();
  const itemVisible = (it: NavItem) => itemVisibleFor(me, it);

  // Groups already filtered by role — if nothing is left, the panel says so instead of showing just the title.
  const groups = selected.groups
    .map((group, gi) => ({ group, gi, items: group.items.filter(itemVisible) }))
    .filter(({ items }) => items.length > 0);

  return (
    <div className='nav-panel-body'>
      {!hideTitle && <div className='nav-panel-title'>{t(selected.titleKey ?? selected.labelKey)}</div>}
      {groups.length === 0 && <p className='nav-panel-empty'>{t('nav.emptyDomain')}</p>}
      {groups.map(({ group, gi, items }) => (
        <div className='nav-group' key={group.labelKey ?? gi}>
          {group.labelKey && <div className='nav-subgroup'>{t(group.labelKey)}</div>}
          {items.map((item) => (
            <PanelItem
              key={item.labelKey}
              icon={item.icon}
              label={t(item.labelKey)}
              active={!!item.to && item.to === activeTo}
              soon={item.soon}
              badge={
                item.to === '/app/requisitions/approval'
                  ? approvalBadge
                  : item.to === '/app/requisitions/triage'
                    ? triageBadge
                    : item.to === '/app/accounting/review'
                      ? reviewPendingBadge
                      : undefined
              }
              onClick={() => onGo(item.to)}
            />
          ))}
        </div>
      ))}
    </div>
  );
}

/**
 * Level-1 row on the phone sheet: icon, label, aggregated count badge, and a chevron —
 * every domain drills into level 2 (see `openMobileDomain`).
 */
function MobileDomainRow({
  domain,
  label,
  badge,
  onClick,
}: {
  domain: NavDomain;
  label: string;
  badge?: number;
  onClick: () => void;
}) {
  return (
    <button className='mnav-row' onClick={onClick}>
      {domain.icon}
      <span className='mnav-row-label'>{label}</span>
      {!!badge && <span className='nav-count-badge'>{badge}</span>}
      <ChevronRight className='mnav-row-chevron' />
    </button>
  );
}

function RailButton({
  icon,
  label,
  active,
  badge,
  onClick,
}: {
  icon: ReactNode;
  label: string;
  active?: boolean;
  /** Aggregated domain count (mock `rdot`); omitted/0 = no dot. */
  badge?: number;
  onClick: () => void;
}) {
  return (
    <button className={`nav-rail-btn ${active ? 'active' : ''}`} onClick={onClick} title={label}>
      {icon}
      <span>{label}</span>
      {!!badge && <span className='nav-rail-dot'>{badge}</span>}
    </button>
  );
}

function PanelItem({
  icon,
  label,
  hint,
  active,
  soon,
  badge,
  onClick,
}: {
  icon: ReactNode;
  label: string;
  hint?: string;
  active?: boolean;
  soon?: boolean;
  /** Item count (mock `ib`); omitted/0 = no badge. */
  badge?: number;
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
      {!!badge && <span className='nav-count-badge'>{badge}</span>}
      {soon && <span className='soon-badge'>{t('common.soon')}</span>}
    </button>
  );
}
