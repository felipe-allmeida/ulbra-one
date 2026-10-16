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
  CheckCircle2,
  Activity,
  CalendarClock,
  LineChart,
  FileText,
  FileBox,
  FolderKanban,
  Target,
  ListTree,
  BookOpen,
  NotebookText,
  Scale,
  Percent,
  Truck,
  UserSquare,
  Tag,
  Landmark,
  Plug,
  ShieldCheck,
  ScrollText,
  KeyRound,
  PiggyBank,
  ClipboardList,
  Users,
  Gauge,
} from 'lucide-react';
import { P, canAny, canSeeFinanceManual, isFinalApprover, type Me, type Permission } from './permissions';

/**
 * Single source of truth for the primary navigation (rail + panel).
 *
 * The rail lists the domains; the panel renders the selected domain's groups
 * and items. Active state and breadcrumbs are derived from the route via
 * `domainForPath` / `itemForPath`, so there is no hand-written `startsWith`
 * ladder anymore — add a screen here and navigation + breadcrumb follow.
 */
/** Role slice beyond permissions (AEL-859): the final approver has READ permissions
 * (core/registry/projects) only for the decision's data — without `audience` they'd see Cadastros/Estrutura.
 * `notPlainUser` hides a surface from the base "Usuário" role only (everyone else, incl. the final approver, sees it).
 * `financeManual` shows a surface ONLY to Controladoria/Financeiro/Presidência (+ Admin) — the CNAB manual. */
export type NavAudience = 'finalApprover' | 'notFinalApprover' | 'notPlainUser' | 'financeManual';

export interface NavItem {
  /** i18n key for the item label. */
  labelKey: string;
  /** Absolute route under `/app`. Omit for a screen that doesn't exist yet. */
  to?: string;
  icon: ReactNode;
  /** Placeholder shown disabled with an "em breve" badge. */
  soon?: boolean;
  /** Permissions that unlock the item (any of them). Absent = visible to all authenticated users. */
  requires?: Permission[];
  /** Role slice (beyond permissions). Absent = everyone. */
  audience?: NavAudience;
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
  /** Permissions that unlock the domain in the rail (any of them). Absent = visible to all. */
  requires?: Permission[];
  /** Role slice (beyond permissions). Absent = everyone. */
  audience?: NavAudience;
  groups: NavGroup[];
}

function audienceOk(me: Me | null | undefined, audience?: NavAudience): boolean {
  if (!audience || me?.isAdmin) return true; // Admin sees everything (same bypass as canAny)
  if (audience === 'notPlainUser') return me?.role !== 'User'; // visible to every cargo except the base "Usuário"
  if (audience === 'financeManual') return canSeeFinanceManual(me); // Controladoria/Financeiro/Presidência only
  return audience === 'finalApprover' ? isFinalApprover(me) : !isFinalApprover(me);
}

/** Domain visibility in the rail: permissions (any) ∧ role slice. */
export function domainVisibleFor(me: Me | null | undefined, d: NavDomain): boolean {
  return canAny(me, d.requires ?? []) && audienceOk(me, d.audience);
}

/** Item visibility in the panel: permissions (any) ∧ role slice. */
export function itemVisibleFor(me: Me | null | undefined, it: NavItem): boolean {
  return canAny(me, it.requires ?? []) && audienceOk(me, it.audience);
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
    audience: 'notFinalApprover',
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
          {
            // Gestão Financeira home (Finance role): queue + remittances + reconciliation + history
            // as tabs. Menu requires treasury:write like the tabs it absorbed (Conciliação/Remessas);
            // the route stays payables:pay|treasury:read for read-only visitors.
            labelKey: 'nav.treasuryHome',
            to: '/app/treasury',
            requires: [P.payablesPay, P.treasuryWrite],
            icon: <Wallet />,
          },
          { labelKey: 'nav.accountsPayable', to: '/app/payables', requires: [P.payablesRead], icon: <ArrowUpRight /> },
          {
            labelKey: 'nav.accountsReceivable',
            to: '/app/receivables',
            requires: [P.receivablesRead],
            icon: <ArrowDownLeft />,
          },
          {
            labelKey: 'nav.pendencies',
            to: '/app/pendencies',
            requires: [P.payablesApprove, P.treasuryApprove],
            icon: <ClipboardCheck />,
          },
          {
            // Conciliação bancária stays a standalone screen (dense OFX import/matching flow); only
            // Remessas was absorbed into the Gestão Financeira home as a tab. Menu requires
            // treasury:write; the route stays treasury:read (the screen has a read-only mode).
            labelKey: 'nav.conciliacao',
            to: '/app/reconciliation',
            requires: [P.treasuryWrite],
            icon: <ArrowLeftRight />,
          },
        ],
      },
      {
        labelKey: 'nav.subgroups.analysis',
        items: [
          { labelKey: 'nav.cashFlow', to: '/app/cash-flow', requires: [P.treasuryRead], icon: <Activity /> },
          { labelKey: 'nav.aging', to: '/app/aging', requires: [P.payablesRead], icon: <CalendarClock /> },
          {
            labelKey: 'nav.paymentsReport',
            to: '/app/payments-report',
            requires: [P.payablesRead, P.treasuryRead],
            icon: <FileText />,
          },
          { labelKey: 'nav.projections', icon: <LineChart />, soon: true },
          { labelKey: 'nav.documents', icon: <FileBox />, soon: true },
        ],
      },
    ],
  },
  {
    key: 'accounting',
    icon: <BookText />,
    labelKey: 'nav.domains.accounting',
    // payables:tax also opens the Contábil section so the Tributário can reach Análise Tributária. Items use any-of
    // (canAny, see domainVisibleFor/itemVisibleFor), so the Tributário — who also holds reports:read + fiscal:read —
    // still sees the report/fiscal items (Balancete/DRE/Balanço, Retenções, Obrigações) but NOT the accounting-write
    // ones (Revisão de Contabilidade, Lançamentos, Plano de Contas — gated on accounting:read).
    requires: [P.accountingRead, P.payablesTax],
    groups: [
      {
        labelKey: 'nav.subgroups.movement',
        items: [
          {
            // Análise tributária — the former Contábil "Despesas" tab, now the Tributário's screen (AEL-948).
            // Kept inside the Contábil section (not a separate nav domain).
            labelKey: 'nav.taxReview',
            to: '/app/tax/review',
            requires: [P.payablesTax],
            icon: <Landmark />,
          },
          {
            // Unified Accounting home (redesign): classification review + expense recording +
            // advances + pending postings in a single screen.
            labelKey: 'nav.accountingReview',
            to: '/app/accounting/review',
            requires: [P.accountingRead],
            icon: <Calculator />,
          },
          {
            labelKey: 'nav.journalEntries',
            to: '/app/accounting/journal-entries',
            requires: [P.accountingRead],
            icon: <BookOpen />,
          },
        ],
      },
      {
        labelKey: 'nav.subgroups.statements',
        items: [
          {
            labelKey: 'nav.journalBook',
            to: '/app/accounting/journal-book',
            requires: [P.accountingRead],
            icon: <BookOpen />,
          },
          {
            labelKey: 'nav.generalLedger',
            to: '/app/accounting/ledger',
            requires: [P.accountingRead],
            icon: <NotebookText />,
          },
          {
            labelKey: 'nav.trialBalance',
            to: '/app/accounting/trial-balance',
            requires: [P.accountingRead, P.reportsRead],
            icon: <Scale />,
          },
          {
            labelKey: 'nav.incomeStatement',
            to: '/app/accounting/income-statement',
            requires: [P.accountingRead, P.reportsRead],
            icon: <LineChart />,
          },
          {
            labelKey: 'nav.balanceSheet',
            to: '/app/accounting/balance-sheet',
            requires: [P.accountingRead, P.reportsRead],
            icon: <BookText />,
          },
          {
            labelKey: 'nav.withholdings',
            to: '/app/accounting/withholdings',
            requires: [P.accountingRead, P.fiscalRead],
            icon: <Percent />,
          },
        ],
      },
      {
        labelKey: 'nav.subgroups.fiscal',
        items: [
          {
            labelKey: 'nav.obrigacoes',
            to: '/app/accounting/obrigacoes',
            requires: [P.fiscalRead],
            icon: <ScrollText />,
          },
        ],
      },
      {
        labelKey: 'nav.subgroups.configuration',
        items: [
          {
            labelKey: 'nav.ledgerAccounts',
            to: '/app/accounting/chart-of-accounts',
            requires: [P.accountingRead],
            icon: <ListTree />,
          },
        ],
      },
    ],
  },
  {
    key: 'budget',
    icon: <PiggyBank />,
    labelKey: 'nav.domains.budget',
    requires: [P.requisitionsRead, P.requisitionsApprove, P.budgetAdjustmentsRead],
    groups: [
      {
        labelKey: 'nav.subgroups.operation',
        items: [
          {
            labelKey: 'nav.myRequisitions',
            to: '/app/requisitions/mine',
            requires: [P.requisitionsWrite],
            icon: <UserSquare />,
          },
          {
            labelKey: 'nav.requisitions',
            to: '/app/requisitions',
            requires: [P.requisitionsRead, P.requisitionsWrite],
            // The final approver works only from the Inbox — the full per-stage list leaves their menu.
            audience: 'notFinalApprover',
            icon: <ClipboardList />,
          },
          {
            labelKey: 'nav.requisitionTriage',
            to: '/app/requisitions/triage',
            requires: [P.requisitionsTriage],
            icon: <ClipboardCheck />,
          },
          {
            // Shared by Approver (approve) + Controllership (triage); parameterized by role inside
            // the screen. Gate on approve|triage, NEVER read (else it leaks to Finance/Accounting/Requester).
            labelKey: 'nav.requisitionOverview',
            to: '/app/requisitions/overview',
            requires: [P.requisitionsApprove, P.requisitionsTriage],
            icon: <LayoutDashboard />,
          },
          {
            labelKey: 'nav.requisitionApproval',
            to: '/app/requisitions/approval',
            requires: [P.requisitionsApprove],
            icon: <CheckCircle2 />,
          },
          {
            labelKey: 'nav.budgets',
            to: '/app/requisitions/budgets',
            requires: [P.requisitionsApprove, P.requisitionsTriage],
            icon: <PiggyBank />,
          },
        ],
      },
      {
        labelKey: 'nav.subgroups.analysis',
        items: [
          {
            labelKey: 'nav.slaReport',
            to: '/app/requisitions/sla-report',
            requires: [P.requisitionsSlaReport],
            audience: 'finalApprover',
            icon: <Gauge />,
          },
        ],
      },
      {
        labelKey: 'nav.subgroups.planning',
        items: [
          {
            labelKey: 'nav.budgetAdjustments',
            to: '/app/budget-adjustments',
            requires: [P.budgetAdjustmentsRead],
            icon: <Scale />,
          },
        ],
      },
    ],
  },
  {
    key: 'registry',
    icon: <Contact />,
    labelKey: 'nav.domains.registry',
    requires: [P.registryRead],
    // The approver has registry:read only for the decision's DATA (vendor name) — no registry menu.
    audience: 'notFinalApprover',
    groups: [
      {
        items: [
          { labelKey: 'nav.vendors', to: '/app/vendors', requires: [P.registryRead], icon: <Truck /> },
          { labelKey: 'nav.customers', to: '/app/customers', requires: [P.registryRead], icon: <UserSquare /> },
          { labelKey: 'nav.employees', to: '/app/employees', requires: [P.registryRead], icon: <Contact /> },
          {
            labelKey: 'nav.expenseCategories',
            to: '/app/expense-categories',
            requires: [P.payablesRead],
            icon: <Tag />,
          },
        ],
      },
    ],
  },
  {
    key: 'structure',
    icon: <Building2 />,
    labelKey: 'nav.domains.structure',
    // Union of the items' permissions (like the finance domain): the panel shows whenever any item can.
    requires: [P.coreRead, P.projectsRead, P.treasuryRead, P.fiscalRead],
    audience: 'notFinalApprover',
    groups: [
      {
        items: [
          { labelKey: 'nav.companiesUnits', to: '/app/structure', requires: [P.coreRead], icon: <Building2 /> },
          { labelKey: 'nav.costCenters', to: '/app/cost-centers', requires: [P.coreRead], icon: <Target /> },
          { labelKey: 'nav.projects', to: '/app/projects', requires: [P.projectsRead], icon: <FolderKanban /> },
          { labelKey: 'nav.bankAccounts', to: '/app/bank-accounts', requires: [P.treasuryRead], icon: <Landmark /> },
          { labelKey: 'nav.retentionRules', to: '/app/retention-rules', requires: [P.fiscalRead], icon: <Percent /> },
        ],
      },
    ],
  },
  {
    // User manual (fluxo + cargos). Footer button pinned ABOVE Settings; visible to every cargo
    // except the base "Usuário" (audience 'notPlainUser'). NOT gated by a permission and NOT wrapped
    // in FinalApproverRedirect, so the Aprovador can open it too.
    key: 'documentation',
    icon: <BookOpen />,
    labelKey: 'nav.domains.documentation',
    titleKey: 'nav.documentation',
    to: '/app/documentation',
    footer: true,
    audience: 'notPlainUser',
    groups: [
      {
        items: [
          { labelKey: 'nav.documentation', to: '/app/documentation', icon: <BookOpen /> },
          // Guias por papel — sem gate: visíveis a todos os cargos que já veem a seção Documentação.
          { labelKey: 'nav.documentationRequester', to: '/app/documentation/guia-solicitante', icon: <UserSquare /> },
          {
            labelKey: 'nav.documentationControllership',
            to: '/app/documentation/guia-controladoria',
            icon: <ClipboardCheck />,
          },
          { labelKey: 'nav.documentationAccounting', to: '/app/documentation/guia-contabil', icon: <BookText /> },
          // Finance-only manual (CNAB Remessa/Retorno) — gated to Controladoria/Financeiro/Presidência (+Admin).
          {
            labelKey: 'nav.documentationFinance',
            to: '/app/documentation/financeiro-remessa-retorno',
            audience: 'financeManual',
            icon: <ArrowLeftRight />,
          },
        ],
      },
    ],
  },
  {
    key: 'settings',
    icon: <Settings />,
    labelKey: 'nav.domains.settings',
    titleKey: 'nav.settings',
    footer: true,
    // For the final approver the Settings panel would only have "em breve" placeholders — leaves the rail.
    audience: 'notFinalApprover',
    groups: [
      {
        items: [
          {
            labelKey: 'nav.roles',
            to: '/app/settings/roles',
            requires: [P.adminUsers],
            icon: <KeyRound />,
          },
          {
            labelKey: 'nav.users',
            to: '/app/settings/users',
            requires: [P.adminUsers],
            icon: <Users />,
          },
          { labelKey: 'nav.integrations', icon: <Plug />, soon: true },
          {
            labelKey: 'nav.audit',
            to: '/app/settings/audit',
            requires: [P.auditRead],
            icon: <ShieldCheck />,
          },
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
