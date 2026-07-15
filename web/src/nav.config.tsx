import type { ReactNode } from 'react';
import {
  LayoutDashboard,
  Calculator,
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
  Percent,
  Truck,
  UserSquare,
  Tag,
  Landmark,
  Plug,
  ShieldCheck,
  ScrollText,
} from 'lucide-react';
import { P, type Permission } from './permissions';

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
  /** Permissões que liberam o item (qualquer uma). Ausente = visível para todos os autenticados. */
  requires?: Permission[];
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
  /** Absolute route the rail button navigates to directly (e.g. the home dashboard). */
  to?: string;
  /** Render the panel as an index of every routable page grouped by domain. */
  index?: boolean;
  /** Pin to the bottom of the rail (e.g. settings). */
  footer?: boolean;
  /** Permissões que liberam o domínio no rail (qualquer uma). Ausente = visível para todos. */
  requires?: Permission[];
  groups: NavGroup[];
}

export const NAV_DOMAINS: NavDomain[] = [
  {
    key: 'home',
    icon: <LayoutDashboard />,
    labelKey: 'nav.domains.home',
    // The rail button navigates straight to the dashboard; the panel is an index
    // of every routable page grouped by domain (see `index`), so there is no
    // redundant single "Início" sub-item.
    to: '/app/home',
    index: true,
    groups: [],
  },
  {
    key: 'finance',
    icon: <Wallet />,
    labelKey: 'nav.domains.finance',
    requires: [P.payablesRead, P.receivablesRead, P.treasuryRead],
    groups: [
      {
        labelKey: 'nav.subgroups.operation',
        items: [
          { labelKey: 'nav.accountsPayable', to: '/app/payables', icon: <ArrowUpRight /> },
          { labelKey: 'nav.accountsReceivable', to: '/app/receivables', icon: <ArrowDownLeft /> },
          { labelKey: 'nav.pendencies', to: '/app/pendencies', icon: <ClipboardCheck /> },
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
    ],
  },
  {
    key: 'accounting',
    icon: <BookText />,
    labelKey: 'nav.domains.accounting',
    requires: [P.accountingRead, P.reportsRead],
    groups: [
      {
        labelKey: 'nav.subgroups.movement',
        items: [
          { labelKey: 'nav.expenseRecording', to: '/app/accounting/expense-recording', icon: <Calculator /> },
          { labelKey: 'nav.journalEntries', to: '/app/accounting/journal-entries', icon: <BookOpen /> },
          { labelKey: 'nav.pendingPostings', to: '/app/accounting/pending', icon: <ClipboardCheck /> },
        ],
      },
      {
        labelKey: 'nav.subgroups.statements',
        items: [
          { labelKey: 'nav.journalBook', to: '/app/accounting/journal-book', icon: <BookOpen /> },
          { labelKey: 'nav.trialBalance', to: '/app/accounting/trial-balance', icon: <Scale /> },
          { labelKey: 'nav.incomeStatement', to: '/app/accounting/income-statement', icon: <LineChart /> },
          { labelKey: 'nav.balanceSheet', to: '/app/accounting/balance-sheet', icon: <BookText /> },
          { labelKey: 'nav.withholdings', to: '/app/accounting/withholdings', icon: <Percent /> },
        ],
      },
      {
        labelKey: 'nav.subgroups.configuration',
        items: [
          { labelKey: 'nav.ledgerAccounts', to: '/app/accounting/chart-of-accounts', icon: <ListTree /> },
          { labelKey: 'nav.costCenters', to: '/app/accounting/cost-centers', icon: <Target /> },
          { labelKey: 'nav.classification', to: '/app/accounting/classification', icon: <Tags /> },
        ],
      },
    ],
  },
  {
    key: 'fiscal',
    icon: <ScrollText />,
    labelKey: 'nav.domains.fiscal',
    requires: [P.fiscalRead],
    groups: [
      {
        labelKey: 'nav.subgroups.obligations',
        items: [{ labelKey: 'nav.obrigacoes', to: '/app/fiscal/obrigacoes', icon: <ScrollText /> }],
      },
    ],
  },
  {
    key: 'registry',
    icon: <Contact />,
    labelKey: 'nav.domains.registry',
    requires: [P.registryRead],
    groups: [
      {
        items: [
          { labelKey: 'nav.vendors', to: '/app/vendors', icon: <Truck /> },
          { labelKey: 'nav.customers', to: '/app/customers', icon: <UserSquare /> },
          { labelKey: 'nav.employees', to: '/app/employees', icon: <Contact /> },
          { labelKey: 'nav.expenseCategories', to: '/app/expense-categories', icon: <Tag /> },
          { labelKey: 'nav.retentionRules', to: '/app/retention-rules', icon: <Percent /> },
          { labelKey: 'nav.bankAccounts', to: '/app/bank-accounts', icon: <Landmark /> },
        ],
      },
    ],
  },
  {
    key: 'structure',
    icon: <Building2 />,
    labelKey: 'nav.domains.structure',
    requires: [P.coreRead],
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

/** Domains that are themselves routable from the rail (e.g. the home dashboard). */
const DOMAIN_ROUTES = NAV_DOMAINS.filter((domain) => domain.to).map((domain) => ({ domain, to: domain.to! }));

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
  // Domain-level routes (e.g. the home dashboard) have no sub-item to match on.
  for (const { domain, to } of DOMAIN_ROUTES) {
    if (isPrefix(to, pathname) && (!best || to.length > best.len)) {
      best = { domain, len: to.length };
    }
  }
  return best?.domain ?? NAV_DOMAINS[0];
}
