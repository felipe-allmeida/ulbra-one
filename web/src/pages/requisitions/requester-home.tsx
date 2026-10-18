import { useEffect, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { ChevronRight, ClipboardList, Plus } from 'lucide-react';
import { type Requisition } from '../../api';
import { useApp } from '../../store';
import { useRequisitions, useRequisitionStageCounts } from '../../queries';
import { P, usePermissions } from '../../permissions';
import { PageHeader, EmptyState, Button, Pager, cx } from '../../ui';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { AsideLayout } from '../../components/workflow';
import { RqListSkeleton } from './list-skeleton';
import { FiscalPendingTag, RequisitionStatusBadge } from './badges';
import { RequesterAside } from './requester-aside';

/**
 * Requester home (AEL-835/836 redesign): "Minhas requisições" as three grouped sections
 * — "Precisa de você" (returned + drafts), "Acompanhando" (in-flight), "Concluídas" (collapsed)
 * — with the shared Solicitante aside (unit budget + queue tiles). Reuses the existing requisition
 * module (scoped to `mine`); this is a reskin, not new workflow logic. Control keeps its card dashboard.
 */
export function RequesterDashboard() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { empresa, empresaId } = useApp();
  const { can } = usePermissions();

  const enabled = { enabled: !!empresaId };
  // One page per section — the rows are paginated (like the per-stage tabs), NOT capped: the pip shows the
  // real server total and the Pager lets the requester reach every row.
  const [needsYouPage, setNeedsYouPage] = useState(1);
  const [trackingPage, setTrackingPage] = useState(1);
  const [nfPendingPage, setNfPendingPage] = useState(1);
  // A fresh company starts every section on page 1.
  useEffect(() => {
    setNeedsYouPage(1);
    setTrackingPage(1);
    setNfPendingPage(1);
  }, [empresaId]);

  // Cada seção agrupa seus status num único GET multi-status (statuses[] OR-set, AEL-876) em vez de um GET por
  // status. As contagens dos pips vêm do stage-counts agregado (1 GROUP BY, deduplicado com a RequesterAside).
  const needsYou = useRequisitions(
    { companyId: empresaId, statuses: ['FinancialComplement', 'Complement', 'Draft'], mine: true, page: needsYouPage },
    enabled,
  );
  const tracking = useRequisitions(
    { companyId: empresaId, statuses: ['Triage', 'Approval', 'InPayment'], mine: true, page: trackingPage },
    enabled,
  );
  // Post-approval requisitions still missing their Nota Fiscal (across InPayment/Paid/Finalized) — the requester
  // has to send the NF so the accounting side can proceed. Not a plain status set (extra "no NF" condition), so
  // it stays its own query.
  const nfPending = useRequisitions(
    { companyId: empresaId, mine: true, awaitingNf: true, page: nfPendingPage },
    enabled,
  );
  const { counts, isLoading: countsLoading } = useRequisitionStageCounts(
    empresaId,
    ['Draft', 'Triage', 'Approval', 'Complement', 'FinancialComplement', 'InPayment', 'Paid', 'Finalized'],
    { mine: true },
  );

  // A requester still fetching sees a spinner instead of the misleading "nada precisa de você" empty text.
  const loading = countsLoading || needsYou.isLoading || tracking.isLoading || nfPending.isLoading;

  const total = (r: { data?: { total: number } }) => r.data?.total ?? 0;
  const items = (r: { data?: { items: Requisition[] } }) => r.data?.items ?? [];

  // "Precisa de você": financial complement (pós-triagem) + devolvidas + rascunhos.
  const needsYouItems = items(needsYou);
  const trackingItems = items(tracking);
  const nfPendingItems = items(nfPending);
  const needsYouCount = (counts.FinancialComplement ?? 0) + (counts.Complement ?? 0) + (counts.Draft ?? 0);
  const trackingCount = (counts.Triage ?? 0) + (counts.Approval ?? 0) + (counts.InPayment ?? 0);
  const nfPendingCount = total(nfPending);
  const doneCount = (counts.Paid ?? 0) + (counts.Finalized ?? 0);

  return (
    <AsideLayout aside={empresa ? <RequesterAside empresaId={empresaId} /> : undefined}>
      <PageHeader
        title={t('requisitions.dashboards.requester.title')}
        sub={t('requisitions.dashboards.requester.subtitle')}
      >
        {can(P.requisitionsWrite) && (
          <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => void nav('/app/requisitions/new')}>
            {t('requisitions.new')}
          </Button>
        )}
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<ClipboardList />}
          title={t('requisitions.empty.noCompany')}
          sub={t('requisitions.empty.noCompanySub')}
        />
      ) : loading ? (
        <>
          <RqListSkeleton label rows={3} />
          <RqListSkeleton label rows={3} />
        </>
      ) : (
        <>
          <Section
            label={t('requisitions.requesterHome.needsYou')}
            count={needsYouCount}
            risk
            rows={needsYouItems}
            empty={t('requisitions.requesterHome.emptyNeedsYou')}
            page={needsYouPage}
            totalPages={needsYou.data?.totalPages ?? 1}
            onPage={setNeedsYouPage}
          />
          {nfPendingCount > 0 && (
            <Section
              label={t('requisitions.requesterHome.nfPending')}
              count={nfPendingCount}
              risk
              rows={nfPendingItems}
              empty={t('requisitions.requesterHome.emptyNfPending')}
              hideNfTag
              page={nfPendingPage}
              totalPages={nfPending.data?.totalPages ?? 1}
              onPage={setNfPendingPage}
            />
          )}
          <Section
            label={t('requisitions.requesterHome.tracking')}
            count={trackingCount}
            rows={trackingItems}
            empty={t('requisitions.requesterHome.emptyTracking')}
            page={trackingPage}
            totalPages={tracking.data?.totalPages ?? 1}
            onPage={setTrackingPage}
          />
          <section className='rq-section'>
            <div className='rq-seclabel'>
              {t('requisitions.requesterHome.done')}
              <span className='rq-pip'>{doneCount}</span>
            </div>
            {doneCount > 0 && (
              <button
                type='button'
                className='rq-collapsed'
                onClick={() => void nav('/app/requisitions?mine=1&stage=encerradas&closed=Paid')}
              >
                <ChevronRight size={15} />
                {t('requisitions.requesterHome.viewDone', { count: doneCount })}
              </button>
            )}
          </section>
        </>
      )}
    </AsideLayout>
  );
}

function Section({
  label,
  count,
  rows,
  empty,
  risk,
  hideNfTag,
  page,
  totalPages,
  onPage,
}: {
  label: string;
  count: number;
  rows: Requisition[];
  empty: string;
  risk?: boolean;
  /** Suppress the per-row "NF pendente" tag when the whole section already means that. */
  hideNfTag?: boolean;
  /** Per-section paging: the pip stays the server total, the Pager reaches every row (nothing is capped). */
  page?: number;
  totalPages?: number;
  onPage?: (page: number) => void;
}) {
  return (
    <section className='rq-section'>
      <div className='rq-seclabel'>
        {label}
        <span className={cx('rq-pip', risk && count > 0 && 'rq-pip--risk')}>{count}</span>
      </div>
      {rows.length === 0 ? (
        <div className='rq-empty'>{empty}</div>
      ) : (
        <>
          <div className='rq-list'>
            {rows.map((r) => (
              <RequesterRow key={r.id} r={r} hideNfTag={hideNfTag} />
            ))}
          </div>
          {page != null && totalPages != null && onPage && totalPages > 1 && (
            <Pager page={page} totalPages={totalPages} onPage={onPage} />
          )}
        </>
      )}
    </section>
  );
}

function RequesterRow({ r, hideNfTag }: { r: Requisition; hideNfTag?: boolean }): ReactNode {
  const { t } = useTranslation();
  const openDetail = useOriginNav();
  const warn = r.status === 'Complement' || r.status === 'FinancialComplement';
  return (
    <button
      type='button'
      className={cx('rq-row', warn && 'rq-row--warn')}
      onClick={() => void openDetail(`/app/requisitions/${r.id}`)}
    >
      {warn && <span className='rq-row-stripe' aria-hidden />}
      <div className='rq-row-main'>
        <div className='rq-row-title'>
          #{r.code} — {r.description}
        </div>
        <div className='rq-row-meta'>{t(`requisitions.type.${r.type}`)}</div>
      </div>
      <div className='rq-row-right'>
        <span className='rq-row-value num'>{brl(r.estimateValue)}</span>
        {!hideNfTag && r.fiscalDocumentPending && <FiscalPendingTag />}
        <RequisitionStatusBadge s={r.status} />
      </div>
      <ChevronRight className='rq-row-chevron' size={16} />
    </button>
  );
}
