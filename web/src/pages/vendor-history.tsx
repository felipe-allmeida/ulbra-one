import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Activity } from 'lucide-react';
import { type Vendor, type RequisitionStatus } from '../api';
import { usePayables, useRequisitions } from '../queries';
import { brl } from '../lib';
import { Input, onRowClick, Pager, SkeletonRows, ToolbarField } from '../ui';
import { P, usePermissions } from '../permissions';
import { useApp } from '../store';
import { fmtDate } from '../datetime';
import { ExpandableRow } from './requisitions/expandable-row';
import { competencyWindowText } from './requisitions/competency';
import { RequisitionStatusBadge } from './requisitions/badges';
import { PayableStatusBadge } from './payables/badges';

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
  const { t } = useTranslation();
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
    <ExpandableRow title='Requisições em andamento' meta={`${total} requisição(ões)`} defaultOpen>
      {q.isLoading ? (
        <table>
          <tbody>
            <SkeletonRows rows={3} cols={4} />
          </tbody>
        </table>
      ) : total === 0 ? (
        <div className='activity-empty'>
          <Activity />
          Nenhuma requisição em andamento no período.
        </div>
      ) : (
        <>
          <div className='cc-activity-table'>
            <table>
              <thead>
                <tr>
                  <th>Código</th>
                  <th>Competência</th>
                  <th style={{ textAlign: 'right' }}>Estimado</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {items.map((r) => (
                  <tr key={r.id} className='row-click' onClick={onRowClick(() => nav(`/app/requisitions/${r.id}`))}>
                    <td className='t-mono'>#{r.code}</td>
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
        <table>
          <tbody>
            <SkeletonRows rows={3} cols={4} />
          </tbody>
        </table>
      ) : total === 0 ? (
        <div className='activity-empty'>
          <Activity />
          Nenhuma conta a pagar no período.
        </div>
      ) : (
        <>
          <div className='cc-activity-table'>
            <table>
              <thead>
                <tr>
                  <th>Vencimento</th>
                  <th>Documento</th>
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
                    <td>{l.documentNumber || '—'}</td>
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
