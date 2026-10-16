import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Wallet } from 'lucide-react';
import { type PayableStageItem, type PayableStatus } from '../../api';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { EmptyState, onRowClick, Pager, Panel, SkeletonRows } from '../../ui';
import { fmtDate } from '../../datetime';
import { SlaChips } from '../../components/workflow';
import { useNames } from './use-names';

// The net only becomes final when accounting completes (withholdings lock at Scheduled).
const isNetLocked = (s: PayableStatus) => s === 'Scheduled' || s === 'Paid';

// "Nota Fiscal pendente": no NF on PendingAccounting/Scheduled/Paid (Advance exempt) — mirrors the backend
// fiscalDocumentPending bucket. The auto-schedule (AEL-846) pays without NF until one is attached.
const isFiscalDocPending = (item: PayableStageItem) =>
  item.hasFiscalDocument === false &&
  item.origin !== 'Advance' &&
  (item.status === 'PendingAccounting' || item.status === 'Scheduled' || item.status === 'Paid');

export function PayableTable({
  companyId,
  stageLabel,
  items,
  total,
  page,
  totalPages,
  onPage,
  extraColumn,
  statusCell,
  showNet,
  loading,
}: {
  companyId: string;
  stageLabel: string;
  items: PayableStageItem[];
  total: number;
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
  extraColumn?: { header: string; cell: (item: PayableStageItem) => ReactNode };
  /** Overrides the status column content (default: stage SLA chips + pendency tags). */
  statusCell?: (item: PayableStageItem) => ReactNode;
  /** Post-accounting tables: renders "Valor bruto" + "Valor líquido" columns; the net cell shows
   * '—' until the withholdings lock (status Scheduled/Paid). */
  showNet?: boolean;
  /** Enquanto o GET carrega: mostra skeleton rows em vez de piscar o estado vazio. */
  loading?: boolean;
}) {
  const { t } = useTranslation();
  const openDetail = useOriginNav();
  const { centroNome } = useNames(companyId);

  // Colunas fixas (7) + líquido + coluna extra — mantém o skeleton alinhado ao cabeçalho.
  const colCount = 7 + (showNet ? 1 : 0) + (extraColumn ? 1 : 0);

  const head = (
    <thead>
      <tr>
        <th>{t('payables.columns.dueDate')}</th>
        <th>{t('payables.columns.description')}</th>
        <th>{t('payables.columns.vendor')}</th>
        <th>{t('payables.columns.costCenter')}</th>
        <th style={{ textAlign: 'center' }}>{t('payables.columns.installments')}</th>
        <th style={{ textAlign: 'right' }}>
          {t(showNet ? 'payables.columns.grossAmount' : 'payables.columns.amount')}
        </th>
        {showNet && <th style={{ textAlign: 'right' }}>{t('payables.columns.netAmount')}</th>}
        <th>{t('payables.columns.status')}</th>
        {extraColumn && <th>{extraColumn.header}</th>}
      </tr>
    </thead>
  );

  if (loading) {
    return (
      <Panel title={stageLabel}>
        <table>
          {head}
          <tbody>
            <SkeletonRows rows={5} cols={colCount} />
          </tbody>
        </table>
      </Panel>
    );
  }

  if (items.length === 0) {
    // No create CTA here: these tables list payables by stage (Contábil review, advances, pendências…), where
    // creating a payable isn't the action; the payables page header already offers "Nova conta" where it belongs.
    return <EmptyState icon={<Wallet />} title={`${stageLabel} — ${t('payables.empty.noItemsTitle')}`} />;
  }

  return (
    <Panel title={`${stageLabel} · ${t('payables.count', { count: total })}`}>
      <table>
        {head}
        <tbody>
          {items.map((l) => (
            <tr
              key={l.payableId}
              className='row-click'
              onClick={onRowClick(() => openDetail(`/app/payables/${l.payableId}`))}
            >
              <td className='t-mono'>{fmtDate(l.firstDueDate)}</td>
              <td>{l.description}</td>
              <td>{l.counterpartyName || '—'}</td>
              <td>{centroNome[l.costCenterId] ?? '—'}</td>
              <td style={{ textAlign: 'center' }}>{l.installmentCount}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(l.totalAmount)}
              </td>
              {showNet && (
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {isNetLocked(l.status) ? brl(l.netTotal) : '—'}
                </td>
              )}
              <td>
                {statusCell ? (
                  statusCell(l)
                ) : (
                  <>
                    <SlaChips stageLabel={stageLabel} dueDate={l.firstDueDate} />
                    {l.origin === 'Advance' && (
                      <span className='badge-status badge-remessa' style={{ marginLeft: 6 }}>
                        {t('payables.tags.advance')}
                      </span>
                    )}
                    {isFiscalDocPending(l) && (
                      <span className='badge-status badge-pendente' style={{ marginLeft: 6 }}>
                        {t('payables.tags.fiscalDocPending')}
                      </span>
                    )}
                  </>
                )}
              </td>
              {extraColumn && <td>{extraColumn.cell(l)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
