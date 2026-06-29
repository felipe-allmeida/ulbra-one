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
    key: 'home',
    icon: <LayoutDashboard />,
    labelKey: 'nav.domains.home',
    groups: [{ items: [{ labelKey: 'nav.home', to: '/app/home', icon: <LayoutDashboard /> }] }],
  },
  {
    key: 'finance',
    icon: <Wallet />,
    labelKey: 'nav.domains.finance',
    groups: [
      {
        labelKey: 'nav.subgroups.operation',
        items: [
          { labelKey: 'nav.accountsPayable', to: '/app/payables', icon: <ArrowUpRight /> },
          { labelKey: 'nav.accountsReceivable', to: '/app/receivables', icon: <ArrowDownLeft /> },
          { labelKey: 'nav.approvals', to: '/app/approvals', icon: <ClipboardCheck /> },
          { labelKey: 'nav.conciliacao', to: '/app/reconciliation', icon: <ArrowLeftRight /> },
          { labelKey: 'nav.remittances', to: '/app/remittances', icon: <SendHorizonal /> },
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
          { labelKey: 'nav.costCenters', to: '/app/cost-centers', icon: <Target /> },
          { labelKey: 'nav.chartAccounts', to: '/app/chart-of-accounts', icon: <ListTree /> },
          { labelKey: 'nav.classification', to: '/app/classification', icon: <Tags /> },
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
          { labelKey: 'nav.journalEntries', to: '/app/accounting/journal-entries', icon: <BookOpen /> },
          { labelKey: 'nav.pendingPostings', to: '/app/accounting/pending', icon: <ClipboardCheck /> },
        ],
      },
      {
        labelKey: 'nav.subgroups.statements',
        items: [
          { labelKey: 'nav.trialBalance', to: '/app/accounting/trial-balance', icon: <Scale /> },
          { labelKey: 'nav.incomeStatement', to: '/app/accounting/income-statement', icon: <LineChart /> },
          { labelKey: 'nav.balanceSheet', to: '/app/accounting/balance-sheet', icon: <BookText /> },
        ],
      },
      {
        labelKey: 'nav.subgroups.configuration',
        items: [{ labelKey: 'nav.ledgerAccounts', to: '/app/accounting/chart-of-accounts', icon: <ListTree /> }],
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
          { labelKey: 'nav.vendors', to: '/app/vendors', icon: <Truck /> },
          { labelKey: 'nav.customers', to: '/app/customers', icon: <UserSquare /> },
          { labelKey: 'nav.employees', to: '/app/employees', icon: <Contact /> },
          { labelKey: 'nav.expenseCategories', to: '/app/expense-categories', icon: <Tag /> },
          { labelKey: 'nav.bankAccounts', to: '/app/bank-accounts', icon: <Landmark /> },
        ],
      },
    ],
  },
  {
    key: 'structure',
    icon: <Building2 />,
    labelKey: 'nav.domains.structure',
    groups: [{ items: [{ labelKey: 'nav.companiesUnits', to: '/app/structure', icon: <Building2 /> }] }],
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
