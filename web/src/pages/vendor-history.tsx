import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Activity } from 'lucide-react';
import { type Vendor, type RequisitionStatus, type Requisition, type PayableListItem } from '../api';
import { usePayables, useRequisitions } from '../queries';
import { brl } from '../lib';
import { Input, LoadingState, Pager, ToolbarField } from '../ui';
import { P, usePermissions } from '../permissions';
import { useApp } from '../store';
import { fmtDate } from '../datetime';
import { ExpandableRow } from './requisitions/expandable-row';
import { competencyWindowText } from './requisitions/competency';
import { RequisitionStatusBadge } from './requisitions/badges';
import { PayableStatusBadge } from './payables/badges';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';

const PAGE_SIZE = 10;

// A requisition becomes a Payable at approval, so listing both double-counts the event — show only
// pre-Payable pipeline stages here (in-payment/paid appear under Contas a pagar).
const PIPELINE_STATUSES: RequisitionStatus[] = ['Draft', 'Triage', 'FinancialComplement', 'Approval', 'Complement'];

const firstDayOfCurrentMonth = (now = new Date()): string =>
  `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
const todayIso = (now = new Date()): string =>
  `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;

interface ListProps {
  companyId: string;
  vendorId: string;
  from: string;
  to: string;
}

/** Vendor history: a date range (defaults to the current month) plus one accordion per type
 * (requisitions in pipeline + payables), each server-paginated and scoped to the vendor. Mirrors
 * CostCenterActivity, swapping the cost-center axis for vendorId and dropping the budget summary. */
export function VendorHistory({ vendor }: { vendor: Vendor }) {
  const [from, setFrom] = useState(firstDayOfCurrentMonth());
  const [to, setTo] = useState(todayIso());
  const { can } = usePermissions();
  // Vendors are global; the history is scoped to the company the user is currently working in.
  const { empresaId } = useApp();
  const canReadRequisitions = can(P.requisitionsRead);
  const canReadPayables = can(P.payablesRead);

  return (
    <>
      <div className='cc-activity-head'>
        <h2 className='section-title'>Histórico</h2>
        <div className='toolbar'>
          <ToolbarField label='De'>
            <Input type='date' aria-label='De' value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
          </ToolbarField>
          <ToolbarField label='Até'>
            <Input type='date' aria-label='Até' value={to} min={from} onChange={(e) => setTo(e.target.value)} />
          </ToolbarField>
        </div>
      </div>

      <div className='cc-activity-groups'>
        {/* key remounts each list on range change, resetting pagination without an effect. */}
        {canReadRequisitions && (
          <RequisitionHistoryList
            key={`req-${empresaId}-${from}-${to}`}
            companyId={empresaId}
            vendorId={vendor.id}
            from={from}
            to={to}
          />
        )}
        {canReadPayables && (
          <PayableHistoryList
            key={`pay-${empresaId}-${from}-${to}`}
            companyId={empresaId}
            vendorId={vendor.id}
            from={from}
            to={to}
          />
        )}
      </div>
    </>
  );
}

/** Requisitions accordion for the vendor, filtered by competency overlapping the date range (month
 * granularity — a requisition's business date is its competency). Post-approval stages are excluded
 * (they show under Contas a pagar). */
function RequisitionHistoryList({ companyId, vendorId, from, to }: ListProps) {
  const nav = useNavigate();
  const [page, setPage] = useState(1);
  const q = useRequisitions(
    {
      companyId,
      vendorId,
      statuses: PIPELINE_STATUSES,
      competencyFrom: from.slice(0, 7),
      competencyTo: to.slice(0, 7),
      page,
      pageSize: PAGE_SIZE,
    },
    { enabled: !!companyId && !!vendorId },
  );
  const items = q.data?.items ?? [];
  const total = q.data?.total ?? 0;
  const totalPages = q.data?.totalPages ?? 1;

  return (
    <ExpandableRow title='Solicitações em andamento' meta={`${total} solicitação(ões)`} defaultOpen>
      {q.isLoading ? (
        <LoadingState />
      ) : total === 0 ? (
        <div className='activity-empty'>
          <Activity />
          Nenhuma solicitação em andamento no período.
        </div>
      ) : (
        <>
          <RequisitionHistoryTable items={items} onRowClick={(r) => nav(`/app/requisitions/${r.id}`)} />
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </>
      )}
    </ExpandableRow>
  );
}

/**
 * Ordem = código/competência/estimado/status, igual a hoje. `código` (identificador único da
 * requisição, curto e sempre presente) é o `title`; `competência` é o único `meta` (contexto de
 * relance); `estimado` é o único valor monetário e vira `amount`; `status` é badge.
 */
function RequisitionHistoryTable({
  items,
  onRowClick,
}: {
  items: Requisition[];
  onRowClick: (r: Requisition) => void;
}) {
  const { t } = useTranslation();
  const columns: Column<Requisition>[] = [
    { key: 'code', header: 'Código', role: 'title', cell: (r) => <span className='t-mono'>#{r.code}</span> },
    {
      key: 'competency',
      header: 'Competência',
      role: 'meta',
      cell: (r) => (
        <span className='t-mono'>{competencyWindowText(r, t('requisitions.competencyWindow.recurring'))}</span>
      ),
    },
    {
      key: 'estimate',
      header: 'Estimado',
      role: 'amount',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brl(r.estimateValue)}</span>,
    },
    { key: 'status', header: 'Status', role: 'status', cell: (r) => <RequisitionStatusBadge s={r.status} /> },
  ];

  return <ResponsiveTable rows={items} columns={columns} rowKey={(r) => r.id} onRowClick={onRowClick} />;
}

/** Payables accordion for the vendor, one row per installment, filtered by due date in the range. */
function PayableHistoryList({ companyId, vendorId, from, to }: ListProps) {
  const nav = useNavigate();
  const [page, setPage] = useState(1);
  const q = usePayables(
    { companyId, vendorId, dueFrom: from, dueTo: to, page, pageSize: PAGE_SIZE },
    { enabled: !!companyId && !!vendorId },
  );
  const items = q.data?.items ?? [];
  const total = q.data?.total ?? 0;
  const totalPages = q.data?.totalPages ?? 1;

  return (
    <ExpandableRow title='Contas a pagar' meta={`${total} parcela(s)`} defaultOpen>
      {q.isLoading ? (
        <LoadingState />
      ) : total === 0 ? (
        <div className='activity-empty'>
          <Activity />
          Nenhuma conta a pagar no período.
        </div>
      ) : (
        <>
          <PayableHistoryTable items={items} onRowClick={(l) => nav(`/app/payables/${l.payableId}`)} />
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </>
      )}
    </ExpandableRow>
  );
}

/**
 * Ordem = vencimento/documento/parcela/valor/status, igual a hoje. `vencimento` (sempre presente,
 * identifica a parcela de relance) é o `title`; `documento` e `parcela` são os dois `meta`;
 * `valor` é o único valor monetário e vira `amount`; `status` é badge.
 */
function PayableHistoryTable({
  items,
  onRowClick,
}: {
  items: PayableListItem[];
  onRowClick: (l: PayableListItem) => void;
}) {
  const columns: Column<PayableListItem>[] = [
    {
      key: 'dueDate',
      header: 'Vencimento',
      role: 'title',
      cell: (l) => <span className='t-mono'>{fmtDate(l.dueDate)}</span>,
    },
    { key: 'document', header: 'Documento', role: 'meta', cell: (l) => l.documentNumber || '—' },
    {
      key: 'installment',
      header: 'Parcela',
      role: 'meta',
      cell: (l) => (
        <span className='t-mono'>
          {l.sequence}/{l.installmentCount}
        </span>
      ),
    },
    {
      key: 'amount',
      header: 'Valor',
      role: 'amount',
      align: 'right',
      cell: (l) => <span className='amount-cell'>{brl(l.amount)}</span>,
    },
    { key: 'status', header: 'Status', role: 'status', cell: (l) => <PayableStatusBadge s={l.payableStatus} /> },
  ];

  return <ResponsiveTable rows={items} columns={columns} rowKey={(l) => l.installmentId} onRowClick={onRowClick} />;
}
