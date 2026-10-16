import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Activity } from 'lucide-react';
import { type CostCenter, type RequisitionStatus } from '../api';
import { usePayables, useRequisitions, useCostCenterBudgetSummary } from '../queries';
import { brl } from '../lib';
import { Input, LoadingState, onRowClick, Pager, Panel, SkeletonRows, ToolbarField } from '../ui';
import { P, usePermissions } from '../permissions';
import { fmtDate } from '../datetime';
import { ExpandableRow } from './requisitions/expandable-row';
import { Tile } from './requisitions/budget-tiles';
import { competencyWindowText, currentCompetency, fmtCompetency } from './requisitions/competency';
import { RequisitionStatusBadge } from './requisitions/badges';
import { PayableStatusBadge } from './payables/badges';

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
        <Tile n={brl(orcado)} l='Orçado' />
        <Tile n={brl(realized)} l='Realizado (gasto)' />
        <Tile n={brl(committed)} l='Empenhado' />
        <Tile n={brl(available)} l='Disponível' tone={available < 0 ? 'bad' : undefined} />
      </div>
      <div className='kpi-foot' style={{ marginTop: 10 }}>
        {orcado > 0 ? `${pct}% do orçado realizado · ` : ''}
        Previsto na fila: {brl(forecast)} · gasto oficial da competência (evita dupla contagem requisição↔pagamento).
      </div>
    </Panel>
  );
}

/** Requisitions accordion for the cost center in the competency (overlapping window), paginated. */
function RequisitionActivityList({ companyId, costCenterId, competency }: ListProps) {
  const { t } = useTranslation();
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
    <ExpandableRow title='Requisições em andamento' meta={`${total} requisição(ões)`} defaultOpen>
      {q.isLoading ? (
        <table>
          <tbody>
            <SkeletonRows rows={3} cols={5} />
          </tbody>
        </table>
      ) : total === 0 ? (
        <div className='activity-empty'>
          <Activity />
          Nenhuma requisição em andamento nesta competência.
        </div>
      ) : (
        <>
          <div className='cc-activity-table'>
            <table>
              <thead>
                <tr>
                  <th>Código</th>
                  <th>Fornecedor</th>
                  <th>Competência</th>
                  <th style={{ textAlign: 'right' }}>Estimado</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {items.map((r) => (
                  <tr key={r.id} className='row-click' onClick={onRowClick(() => nav(`/app/requisitions/${r.id}`))}>
                    <td className='t-mono'>#{r.code}</td>
                    <td>{r.vendorName || '—'}</td>
                    <td className='t-mono'>{competencyWindowText(r, t('requisitions.competencyWindow.recurring'))}</td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {brl(r.estimateValue)}
                    </td>
                    <td>
                      <RequisitionStatusBadge s={r.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </>
      )}
    </ExpandableRow>
  );
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
        <table>
          <tbody>
            <SkeletonRows rows={3} cols={5} />
          </tbody>
        </table>
      ) : total === 0 ? (
        <div className='activity-empty'>
          <Activity />
          Nenhuma conta a pagar nesta competência.
        </div>
      ) : (
        <>
          <div className='cc-activity-table'>
            <table>
              <thead>
                <tr>
                  <th>Vencimento</th>
                  <th>Favorecido</th>
                  <th>Parcela</th>
                  <th style={{ textAlign: 'right' }}>Valor</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {items.map((l) => (
                  <tr
                    key={l.installmentId}
                    className='row-click'
                    onClick={onRowClick(() => nav(`/app/payables/${l.payableId}`))}
                  >
                    <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                    <td>
                      {l.counterpartyName}
                      {l.documentNumber ? ` · ${l.documentNumber}` : ''}
                    </td>
                    <td className='t-mono'>
                      {l.sequence}/{l.installmentCount}
                    </td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {brl(l.amount)}
                    </td>
                    <td>
                      <PayableStatusBadge s={l.payableStatus} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </>
      )}
    </ExpandableRow>
  );
}
