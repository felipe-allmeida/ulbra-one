import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Activity } from 'lucide-react';
import { type CostCenter, type RequisitionStatus, type Requisition, type PayableListItem } from '../api';
import { usePayables, useRequisitions, useCostCenterBudgetSummary } from '../queries';
import { brl } from '../lib';
import { Input, LoadingState, Pager, Panel, ToolbarField } from '../ui';
import { P, usePermissions } from '../permissions';
import { fmtDate } from '../datetime';
import { ExpandableRow } from './requisitions/expandable-row';
import { money, Tile } from './requisitions/budget-tiles';
import { competencyWindowText, currentCompetency, fmtCompetency } from './requisitions/competency';
import { RequisitionStatusBadge } from './requisitions/badges';
import { PayableStatusBadge } from './payables/badges';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';

const PAGE_SIZE = 10;

// A requisition becomes a Payable at approval (→ InPayment). To avoid listing the same economic event
// twice (once as the requisition, once as its Payable), the requisitions accordion shows only the
// in-pipeline stages (no Payable yet); once scheduled/paid it appears under Contas a pagar — which is
// also when the budget is actually consumed.
const PIPELINE_STATUSES: RequisitionStatus[] = ['Draft', 'Triage', 'FinancialComplement', 'Approval', 'Complement'];

interface ListProps {
  companyId: string;
  costCenterId: string;
  competency: string;
}

/** Cost-center activity: competency filter, an authoritative spend summary, and one accordion per type
 * (requisitions + payables), each server-paginated. Replaces the old list backed by the legacy
 * /transactions endpoint (never written by the current flow). */
export function CostCenterActivity({ centro }: { centro: CostCenter }) {
  const [competency, setCompetency] = useState(currentCompetency());
  // Payables accordion needs payables:read — roles like Requester reach this screen (core:read) but
  // lack it; mounting it would 401/403.
  const canReadPayables = usePermissions().can(P.payablesRead);

  return (
    <>
      <div className='cc-activity-head'>
        <h2 className='section-title'>Atividade</h2>
        <div className='toolbar'>
          <ToolbarField label='Competência'>
            <Input
              type='month'
              aria-label='Competência'
              value={competency}
              onChange={(e) => setCompetency(e.target.value)}
            />
          </ToolbarField>
        </div>
      </div>

      <CompetencySummaryStrip companyId={centro.companyId} costCenterId={centro.id} competency={competency} />

      <div className='cc-activity-groups'>
        {/* key={competency} remounts the list on competency change, resetting pagination without an effect. */}
        <RequisitionActivityList
          key={`req-${competency}`}
          companyId={centro.companyId}
          costCenterId={centro.id}
          competency={competency}
        />
        {canReadPayables && (
          <PayableActivityList
            key={`pay-${competency}`}
            companyId={centro.companyId}
            costCenterId={centro.id}
            competency={competency}
          />
        )}
      </div>
    </>
  );
}

/** Competency summary: budget × realized × committed × available. Single authoritative source of the
 * cost center's spend — avoids the requisition↔payment double count (realized is computed from the
 * requisition side only, in BudgetControlService). */
function CompetencySummaryStrip({ companyId, costCenterId, competency }: ListProps) {
  const q = useCostCenterBudgetSummary(companyId, competency);
  if (q.isLoading) return <LoadingState />;
  const line = q.data?.find((l) => l.costCenterId === costCenterId);
  const orcado = line?.orcado ?? 0;
  const realized = line?.realized ?? 0;
  const committed = line?.committed ?? 0;
  const forecast = line?.forecast ?? 0;
  const available = line?.available ?? 0;
  const pct = orcado > 0 ? Math.round((realized / orcado) * 100) : 0;
  const semOrcamento = !!line && !line.budgeted;

  return (
    <Panel title={`Competência ${fmtCompetency(competency)}${semOrcamento ? ' · sem orçamento' : ''}`} padded>
      <div className='kpitiles'>
        <Tile {...money(orcado)} l='Orçado' />
        <Tile {...money(realized)} l='Realizado (gasto)' />
        <Tile {...money(committed)} l='Empenhado' />
        <Tile {...money(available)} l='Disponível' tone={available < 0 ? 'bad' : undefined} />
      </div>
      <div className='kpi-foot' style={{ marginTop: 10 }}>
        {orcado > 0 ? `${pct}% do orçado realizado · ` : ''}
        Previsto na fila: {brl(forecast)} · gasto oficial da competência (evita dupla contagem solicitação↔pagamento).
      </div>
    </Panel>
  );
}

/** Requisitions accordion for the cost center in the competency (overlapping window), paginated. */
function RequisitionActivityList({ companyId, costCenterId, competency }: ListProps) {
  const nav = useNavigate();
  const [page, setPage] = useState(1);
  const q = useRequisitions(
    { companyId, costCenterId, competency, statuses: PIPELINE_STATUSES, page, pageSize: PAGE_SIZE },
    { enabled: !!companyId && !!costCenterId },
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
          Nenhuma solicitação em andamento nesta competência.
        </div>
      ) : (
        <>
          <RequisitionActivityTable items={items} onRowClick={(r) => nav(`/app/requisitions/${r.id}`)} />

          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </>
      )}
    </ExpandableRow>
  );
}

/**
 * Ordem = código/fornecedor/competência/estimado/status, igual a hoje. `código` (identificador
 * único, curto e sempre presente) é o `title`; `fornecedor` e `competência` são os dois `meta`;
 * `estimado` é o único valor monetário e vira `amount`; `status` é badge.
 */
function RequisitionActivityTable({
  items,
  onRowClick,
}: {
  items: Requisition[];
  onRowClick: (r: Requisition) => void;
}) {
  const { t } = useTranslation();
  const columns: Column<Requisition>[] = [
    { key: 'code', header: 'Código', role: 'title', cell: (r) => <span className='t-mono'>#{r.code}</span> },
    { key: 'vendor', header: 'Fornecedor', role: 'meta', cell: (r) => r.vendorName || '—' },
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

/** Payables accordion for the cost center in the competency (IssueDate in-month), one row per installment, paginated. */
function PayableActivityList({ companyId, costCenterId, competency }: ListProps) {
  const nav = useNavigate();
  const [page, setPage] = useState(1);
  const q = usePayables(
    { companyId, costCenterId, competency, page, pageSize: PAGE_SIZE },
    { enabled: !!companyId && !!costCenterId },
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
          Nenhuma conta a pagar nesta competência.
        </div>
      ) : (
        <>
          <PayableActivityTable items={items} onRowClick={(l) => nav(`/app/payables/${l.payableId}`)} />

          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </>
      )}
    </ExpandableRow>
  );
}

/**
 * Ordem = vencimento/favorecido/parcela/valor/status, igual a hoje. `vencimento` (sempre presente,
 * identifica a parcela de relance) é o `title`; `favorecido` e `parcela` são os dois `meta`;
 * `valor` é o único valor monetário e vira `amount`; `status` é badge.
 */
function PayableActivityTable({
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
    {
      key: 'counterparty',
      header: 'Favorecido',
      role: 'meta',
      cell: (l) => (
        <>
          {l.counterpartyName}
          {l.documentNumber ? ` · ${l.documentNumber}` : ''}
        </>
      ),
    },
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
