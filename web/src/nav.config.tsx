import type { ReactNode } from 'react';
import {
  LayoutDashboard,
  Wallet,
  BookText,
  Contact,
  Building2,
  Settings,
  ArrowUpRight,
  ArrowDownLeft,
  ClipboardCheck,
  ArrowLeftRight,
  SendHorizonal,
  Activity,
  CalendarClock,
  LineChart,
  FileText,
  FileBox,
  Target,
  ListTree,
  Tags,
  BookOpen,
  Scale,
  Truck,
  UserSquare,
  Tag,
  Landmark,
  Plug,
  ShieldCheck,
} from 'lucide-react';

/**
 * Single source of truth for the primary navigation (rail + panel).
 *
 * The rail lists the domains; the panel renders the selected domain's groups
 * and items. Active state and breadcrumbs are derived from the route via
 * `domainForPath` / `itemForPath`, so there is no hand-written `startsWith`
 * ladder anymore — add a screen here and navigation + breadcrumb follow.
 */
export interface NavItem {
  /** i18n key for the item label. */
  labelKey: string;
  /** Absolute route under `/app`. Omit for a screen that doesn't exist yet. */
  to?: string;
  icon: ReactNode;
  /** Placeholder shown disabled with an "em breve" badge. */
  soon?: boolean;
}

export interface NavGroup {
  /** i18n key for the subgroup header. Omit to render items without a header. */
  labelKey?: string;
  items: NavItem[];
}

export interface NavDomain {
  key: string;
  icon: ReactNode;
  /** i18n key for the short rail label. */
  labelKey: string;
  /** i18n key for the panel title; falls back to `labelKey`. */
  titleKey?: string;
  /** Pin to the bottom of the rail (e.g. settings). */
  footer?: boolean;
  groups: NavGroup[];
}

export const NAV_DOMAINS: NavDomain[] = [
  {
    key: 'inicio',
    icon: <LayoutDashboard />,
    labelKey: 'nav.domains.home',
    groups: [{ items: [{ labelKey: 'nav.home', to: '/app/inicio', icon: <LayoutDashboard /> }] }],
  },
  {
    key: 'finance',
    icon: <Wallet />,
    labelKey: 'nav.domains.finance',
    groups: [
      {
        labelKey: 'nav.subgroups.operation',
        items: [
          { labelKey: 'nav.accountsPayable', to: '/app/contas-a-pagar', icon: <ArrowUpRight /> },
          { labelKey: 'nav.accountsReceivable', to: '/app/contas-a-receber', icon: <ArrowDownLeft /> },
          { labelKey: 'nav.approvals', to: '/app/aprovacoes', icon: <ClipboardCheck /> },
          { labelKey: 'nav.conciliacao', to: '/app/conciliacao', icon: <ArrowLeftRight /> },
          { labelKey: 'nav.remittances', to: '/app/remessas', icon: <SendHorizonal /> },
        ],
      },
      {
        labelKey: 'nav.subgroups.analysis',
        items: [
          { labelKey: 'nav.cashFlow', to: '/app/cash-flow', icon: <Activity /> },
          { labelKey: 'nav.aging', to: '/app/aging', icon: <CalendarClock /> },
          { labelKey: 'nav.projections', icon: <LineChart />, soon: true },
          { labelKey: 'nav.reports', icon: <FileText />, soon: true },
          { labelKey: 'nav.documents', icon: <FileBox />, soon: true },
        ],
      },
      {
        labelKey: 'nav.subgroups.configuration',
        items: [
          { labelKey: 'nav.costCenters', to: '/app/centros', icon: <Target /> },
          { labelKey: 'nav.chartAccounts', to: '/app/plano-de-contas', icon: <ListTree /> },
          { labelKey: 'nav.classification', to: '/app/classificacao', icon: <Tags /> },
        ],
      },
    ],
  },
  {
    key: 'accounting',
    icon: <BookText />,
    labelKey: 'nav.domains.accounting',
    groups: [
      {
        labelKey: 'nav.subgroups.movement',
        items: [
          { labelKey: 'nav.journalEntries', to: '/app/contabil/lancamentos', icon: <BookOpen /> },
          { labelKey: 'nav.pendingPostings', to: '/app/contabil/pendentes', icon: <ClipboardCheck /> },
        ],
      },
      {
        labelKey: 'nav.subgroups.statements',
        items: [
          { labelKey: 'nav.trialBalance', to: '/app/contabil/balancete', icon: <Scale /> },
          { labelKey: 'nav.incomeStatement', to: '/app/contabil/dre', icon: <LineChart /> },
          { labelKey: 'nav.balanceSheet', to: '/app/contabil/balanco', icon: <BookText /> },
        ],
      },
      {
        labelKey: 'nav.subgroups.configuration',
        items: [{ labelKey: 'nav.ledgerAccounts', to: '/app/contabil/plano-de-contas', icon: <ListTree /> }],
      },
    ],
  },
  {
    key: 'registry',
    icon: <Contact />,
    labelKey: 'nav.domains.registry',
    groups: [
      {
        items: [
          { labelKey: 'nav.vendors', to: '/app/fornecedores', icon: <Truck /> },
          { labelKey: 'nav.customers', to: '/app/clientes', icon: <UserSquare /> },
          { labelKey: 'nav.employees', to: '/app/colaboradores', icon: <Contact /> },
          { labelKey: 'nav.expenseCategories', to: '/app/categorias-despesa', icon: <Tag /> },
          { labelKey: 'nav.bankAccounts', to: '/app/contas-bancarias', icon: <Landmark /> },
        ],
      },
    ],
  },
  {
    key: 'structure',
    icon: <Building2 />,
    labelKey: 'nav.domains.structure',
    groups: [{ items: [{ labelKey: 'nav.companiesUnits', to: '/app/estrutura', icon: <Building2 /> }] }],
  },
  {
    key: 'settings',
    icon: <Settings />,
    labelKey: 'nav.domains.settings',
    titleKey: 'nav.settings',
    footer: true,
    groups: [
      {
        items: [
          { labelKey: 'nav.integrations', icon: <Plug />, soon: true },
          { labelKey: 'nav.audit', icon: <ShieldCheck />, soon: true },
          { labelKey: 'nav.settings', icon: <Settings />, soon: true },
        ],
      },
    ],
  },
];

/** Every routable item, flattened. Order is irrelevant — matching is longest-prefix. */
const ROUTABLE = NAV_DOMAINS.flatMap((domain) =>
  domain.groups.flatMap((group) => group.items.filter((item) => item.to).map((item) => ({ domain, item }))),
);

function isPrefix(to: string, pathname: string): boolean {
  return pathname === to || pathname.startsWith(`${to}/`);
}

/** The item whose route is the longest prefix of `pathname`, or null. */
export function itemForPath(pathname: string): NavItem | null {
  let best: { item: NavItem; len: number } | null = null;
  for (const { item } of ROUTABLE) {
    if (item.to && isPrefix(item.to, pathname) && (!best || item.to.length > best.len)) {
      best = { item, len: item.to.length };
    }
  }
  return best?.item ?? null;
}

/** The domain that owns the route at `pathname`, falling back to the first domain. */
export function domainForPath(pathname: string): NavDomain {
  let best: { domain: NavDomain; len: number } | null = null;
  for (const { domain, item } of ROUTABLE) {
    if (item.to && isPrefix(item.to, pathname) && (!best || item.to.length > best.len)) {
      best = { domain, len: item.to.length };
    }
  }
  return best?.domain ?? NAV_DOMAINS[0];
}
