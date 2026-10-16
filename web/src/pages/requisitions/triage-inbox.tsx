import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { ChevronRight, ClipboardCheck, ListChecks, Search } from 'lucide-react';
import { type Requisition, type RequisitionDecision } from '../../api';
import { useApp } from '../../store';
import { useMyRequisitionTriageDecisions, useProjects, useRequisitions, useUnits } from '../../queries';
import { Button, EmptyState, PageHeader, Pager } from '../../ui';
import { brl } from '../../lib';
import { fmtDate } from '../../datetime';
import { RoleHome, StageTabBar, type StageTab } from '../../components/workflow';
import { RqListSkeleton } from './list-skeleton';
import { RequisitionStatusBadge, RequisitionSlaChip } from './badges';
import { useBudgetBadge } from './budget-guard';
import { ControllershipAside } from './controllership-aside';
import { TriageSequence } from './triage-sequence';
import { competencyWindowText } from './competency';

type InboxTab = 'action' | 'all' | 'history';

/**
 * Controllership Inbox (Entrega 4) — the TRIAGE queue. Three tabs: 'Precisa de você' (Triage status, with
 * over-budget/overdue flags), 'Todas' (macro-sections from Controllership's vantage) and 'Histórico' (the
 * controller's own triage decisions, by month). Focus is validating vendor + values; an overage is a hint,
 * not a block (resolved via a Budget Adjustment). Mirrors the Approver Inbox (AEL-859) one stage earlier.
 */
export function RequisitionTriageInbox() {
  const { t } = useTranslation();
  const { empresa, empresaId, empresas, isAllCompanies, scopeReady } = useApp();
  const companyName = (id: string) => empresas.find((e) => e.id === id)?.legalName ?? '—';
  const [tab, setTab] = useState<InboxTab>('action');
  const [reviewOpen, setReviewOpen] = useState(false);

  // Search WITHIN the queue (keeps each tab's status) — 250ms debounce, treasury/home pattern.
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(id);
  }, [search]);

  const triage = useRequisitions(
    { companyId: empresaId, status: 'Triage', mine: false, page: 1 },
    { enabled: !!empresaId },
  );
  const pendingCount = triage.data?.total ?? 0;

  const tabs: StageTab[] = [
    {
      key: 'action',
      label: t('requisitions.triageInbox.tabs.needsYou'),
      count: pendingCount,
      badge: pendingCount > 0 ? 'warning' : undefined,
    },
    { key: 'all', label: t('requisitions.triageInbox.tabs.all') },
    { key: 'history', label: t('requisitions.triageInbox.tabs.history') },
  ];

  return (
    <>
      <PageHeader title={t('requisitions.triageInbox.title')} sub={t('requisitions.triageInbox.subtitle')}>
        {scopeReady && tab !== 'history' && (
          <div className='top-search' style={{ width: 260 }}>
            <Search />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('requisitions.searchPlaceholder')}
              aria-label={t('requisitions.searchPlaceholder')}
            />
          </div>
        )}
        {/* Read-only in the consolidated "Todas" view (AEL-906): the bulk-review flow needs a single company. */}
        {tab === 'action' && pendingCount > 0 && !isAllCompanies && (
          <Button icon={<ListChecks />} onClick={() => setReviewOpen(true)}>
            {t('requisitions.review.start')}
          </Button>
        )}
      </PageHeader>

      {!scopeReady ? (
        <EmptyState
          icon={<ClipboardCheck />}
          title={t('requisitions.empty.noCompany')}
          sub={t('requisitions.empty.noCompanySub')}
        />
      ) : (
        // Aside hidden in "Todas" — its budget/projection panels are per-company (AEL-906).
        <RoleHome aside={empresa ? <ControllershipAside empresaId={empresaId} showQueueProjection /> : undefined}>
          <StageTabBar tabs={tabs} active={tab} onChange={(k) => setTab(k as InboxTab)} />
          {tab === 'action' && (
            <NeedsYouTab
              companyId={empresaId}
              search={debouncedSearch}
              isAllCompanies={isAllCompanies}
              companyName={companyName}
            />
          )}
          {tab === 'all' && (
            <AllTab
              companyId={empresaId}
              search={debouncedSearch}
              isAllCompanies={isAllCompanies}
              companyName={companyName}
            />
          )}
          {tab === 'history' && <HistoryTab companyId={empresaId} />}
        </RoleHome>
      )}

      {reviewOpen && <TriageSequence companyId={empresaId} onClose={() => setReviewOpen(false)} />}
    </>
  );
}

/** Tab "Precisa de você": triage queue (whole company), with vendor + per-row flags. */
function NeedsYouTab({
  companyId,
  search,
  isAllCompanies,
  companyName,
}: {
  companyId: string;
  search: string;
  isAllCompanies?: boolean;
  companyName: (id: string) => string;
}) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId, search]);
  const list = useRequisitions(
    { companyId, status: 'Triage', mine: false, page, search: search || undefined },
    { enabled: !!companyId },
  );
  const units = useUnits(companyId).data ?? [];
  const projects = useProjects(companyId).data ?? [];
  const items = list.data?.items ?? [];

  if (list.isLoading) return <RqListSkeleton rows={5} />;
  if (items.length === 0) {
    return <div className='rq-empty'>{t('requisitions.triageInbox.emptyNeedsYou')}</div>;
  }

  return (
    <>
      <div className='rq-list'>
        {items.map((r) => (
          <TriageRow
            key={r.id}
            r={r}
            units={units}
            projects={projects}
            company={isAllCompanies ? companyName(r.companyId) : undefined}
          />
        ))}
      </div>
      <Pager page={page} totalPages={list.data?.totalPages ?? 1} onPage={setPage} />
    </>
  );
}

interface NameList {
  units: { id: string; acronym: string }[];
  projects: { id: string; name: string }[];
}

/** Triage-queue row: vendor + meta (company·type·unit·project·competency) + amount + flags. */
function TriageRow({ r, units, projects, company }: { r: Requisition; company?: string } & NameList) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { badge } = useBudgetBadge(r);

  // vendorName comes denormalized in the list response (AEL-876) — no per-row lookup.
  const title = r.vendorName || `#${r.code}`;
  const meta = [
    company,
    t(`requisitions.type.${r.type}`),
    units.find((u) => u.id === r.unitId)?.acronym,
    r.projectId ? projects.find((p) => p.id === r.projectId)?.name : undefined,
    competencyWindowText(r, t('requisitions.competencyWindow.recurring')),
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <button
      type='button'
      className='rq-row'
      onClick={() => void nav(`/app/requisitions/triage/${r.id}`, { state: { from: '/app/requisitions/triage' } })}
    >
      {badge !== 'none' && (
        <span
          className='rq-row-stripe'
          style={{ background: badge === 'over' ? 'var(--danger)' : 'var(--warning)' }}
          aria-hidden
        />
      )}
      <div className='rq-row-main'>
        <div className='rq-row-title'>{title}</div>
        {/* Description (the "what") right below the vendor. */}
        <div className='rq-row-meta' style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {r.description}
        </div>
        <div className='rq-row-meta'>{meta}</div>
      </div>
      <div className='rq-row-right'>
        <span className='rq-row-value num'>{brl(r.estimateValue)}</span>
        {badge === 'over' && (
          <span className='badge-status badge-rejeitado'>{t('requisitions.triageInbox.overBudget')}</span>
        )}
        {badge === 'unbudgeted' && (
          <span className='badge-status badge-pendente'>{t('requisitions.triageInbox.unbudgeted')}</span>
        )}
        <RequisitionSlaChip req={r} />
      </div>
      <ChevronRight className='rq-row-chevron' size={16} />
    </button>
  );
}

/** Tab "Todas" — macro-sections from Controllership's vantage: awaiting you (Triage) / in progress
 * (downstream) / done. One multi-status query per section (statuses[] OR-set); pip = the query's total. */
function AllTab({
  companyId,
  search,
  isAllCompanies,
  companyName,
}: {
  companyId: string;
  search: string;
  isAllCompanies?: boolean;
  companyName: (id: string) => string;
}) {
  const { t } = useTranslation();
  const units = useUnits(companyId).data ?? [];
  const opts = { enabled: !!companyId };
  // One page per macro-section — rows are paginated (like the per-stage tabs), NOT capped; pip = server total.
  const [awaitingPage, setAwaitingPage] = useState(1);
  const [progressPage, setProgressPage] = useState(1);
  const [donePage, setDonePage] = useState(1);
  useEffect(() => {
    setAwaitingPage(1);
    setProgressPage(1);
    setDonePage(1);
  }, [companyId, search]);

  // Each macro-section is ONE multi-status query (statuses[] OR-set, AEL-876) instead of one query per status.
  // From Controllership's seat, "awaiting you" = Triage; kept singular so it dedupes with the inbox count probe.
  const awaitingYou = useRequisitions(
    { companyId, status: 'Triage', mine: false, page: awaitingPage, search: search || undefined },
    opts,
  );
  const inProgress = useRequisitions(
    {
      companyId,
      statuses: ['FinancialComplement', 'Complement', 'Approval', 'InPayment'],
      mine: false,
      page: progressPage,
      search: search || undefined,
    },
    opts,
  );
  const done = useRequisitions(
    {
      companyId,
      statuses: ['Paid', 'Finalized', 'Cancelled', 'AwaitingFiscalDocument'],
      mine: false,
      page: donePage,
      search: search || undefined,
    },
    opts,
  );

  const groups = [
    { key: 'awaitingYou', risk: true, list: awaitingYou, page: awaitingPage, onPage: setAwaitingPage },
    { key: 'inProgress', risk: false, list: inProgress, page: progressPage, onPage: setProgressPage },
    { key: 'done', risk: false, list: done, page: donePage, onPage: setDonePage },
  ];
  const allEmpty = groups.every((g) => (g.list.data?.items.length ?? 0) === 0);
  if (groups.some((g) => g.list.isLoading))
    return (
      <>
        <RqListSkeleton label rows={3} />
        <RqListSkeleton label rows={3} />
      </>
    );
  if (allEmpty) return <div className='rq-empty'>{t('requisitions.triageInbox.emptyAll')}</div>;

  return (
    <>
      {groups.map((g) => {
        const items = g.list.data?.items ?? [];
        if (items.length === 0) return null;
        const total = g.list.data?.total ?? 0;
        const totalPages = g.list.data?.totalPages ?? 1;
        return (
          <section key={g.key} className='rq-section'>
            <div className='rq-seclabel'>
              {t(`requisitions.triageInbox.sections.${g.key}`)}
              <span className={g.risk ? 'rq-pip rq-pip--risk' : 'rq-pip'}>{total}</span>
            </div>
            <div className='rq-list'>
              {items.map((r) => (
                <ReadOnlyRow
                  key={r.id}
                  r={r}
                  units={units}
                  company={isAllCompanies ? companyName(r.companyId) : undefined}
                />
              ))}
            </div>
            {totalPages > 1 && <Pager page={g.page} totalPages={totalPages} onPage={g.onPage} />}
          </section>
        );
      })}
    </>
  );
}

/** Read-only row (Todas tab): code+description, [company·]type·unit, amount + status badge → decision. */
function ReadOnlyRow({
  r,
  units,
  company,
}: {
  r: Requisition;
  units: { id: string; acronym: string }[];
  company?: string;
}) {
  const { t } = useTranslation();
  const nav = useNavigate();
  return (
    <button
      type='button'
      className='rq-row'
      onClick={() => void nav(`/app/requisitions/triage/${r.id}`, { state: { from: '/app/requisitions/triage' } })}
    >
      <div className='rq-row-main'>
        <div className='rq-row-title'>
          #{r.code} — {r.description}
        </div>
        <div className='rq-row-meta'>
          {[company, t(`requisitions.type.${r.type}`), units.find((u) => u.id === r.unitId)?.acronym]
            .filter(Boolean)
            .join(' · ')}
        </div>
      </div>
      <div className='rq-row-right'>
        <span className='rq-row-value num'>{brl(r.estimateValue)}</span>
        <RequisitionStatusBadge s={r.status} />
      </div>
      <ChevronRight className='rq-row-chevron' size={16} />
    </button>
  );
}

const DECISION_FLAG: Record<string, { labelKey: string; cls: string }> = {
  ApproveTriage: { labelKey: 'requisitions.triageInbox.history.forwarded', cls: 'badge-pago' },
  ReturnForComplement: { labelKey: 'requisitions.triageInbox.history.returned', cls: 'badge-pendente' },
  Cancel: { labelKey: 'requisitions.triageInbox.history.rejected', cls: 'badge-rejeitado' },
};

/** Tab "Histórico" — the controller's own triage decisions (my-triage-decisions), by month. */
function HistoryTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const [page, setPage] = useState(1);
  const list = useMyRequisitionTriageDecisions(companyId, page);
  const items = list.data?.items ?? [];

  if (list.isLoading) return <RqListSkeleton label rows={4} />;
  if (items.length === 0) {
    return <div className='rq-empty'>{t('requisitions.triageInbox.history.empty')}</div>;
  }

  // Group by decision month (local tz, same for key and label), preserving the server order.
  const groups = new Map<string, RequisitionDecision[]>();
  for (const d of items) {
    const dt = new Date(d.decidedAt);
    const key = `${dt.getFullYear()}-${dt.getMonth()}`;
    const bucket = groups.get(key);
    if (bucket) bucket.push(d);
    else groups.set(key, [d]);
  }
  const monthLabel = (iso: string) => {
    const s = new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric' }).format(new Date(iso));
    return s.charAt(0).toUpperCase() + s.slice(1);
  };

  return (
    <>
      {[...groups.entries()].map(([month, decisions]) => (
        <section key={month} className='rq-section'>
          <div className='rq-seclabel'>
            {monthLabel(decisions[0].decidedAt)}
            <span className='rq-pip'>{decisions.length}</span>
          </div>
          <div className='rq-list'>
            {decisions.map((d, i) => {
              const flag = DECISION_FLAG[d.decisionAction];
              return (
                <button
                  key={`${d.requisitionId}-${d.decidedAt}-${i}`}
                  type='button'
                  className='rq-row'
                  onClick={() =>
                    void nav(`/app/requisitions/triage/${d.requisitionId}`, {
                      state: { from: '/app/requisitions/triage' },
                    })
                  }
                >
                  <div className='rq-row-main'>
                    <div className='rq-row-title'>{d.vendorName || `#${d.code}`}</div>
                    <div
                      className='rq-row-meta'
                      style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                    >
                      {fmtDate(d.decidedAt)} · {d.description}
                    </div>
                  </div>
                  <div className='rq-row-right'>
                    <span className='rq-row-value num'>{brl(d.estimateValue)}</span>
                    {flag && <span className={`badge-status ${flag.cls}`}>{t(flag.labelKey)}</span>}
                  </div>
                  <ChevronRight className='rq-row-chevron' size={16} />
                </button>
              );
            })}
          </div>
        </section>
      ))}
      <Pager page={page} totalPages={list.data?.totalPages ?? 1} onPage={setPage} />
    </>
  );
}
