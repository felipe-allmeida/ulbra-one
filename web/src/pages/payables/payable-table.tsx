import { type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Plus, Wallet } from 'lucide-react';
import { type PayableStageItem } from '../../api';
import { brl } from '../../lib';
import { Panel, Pager, EmptyState } from '../../ui';
import { fmtDate } from '../../datetime';
import { SlaChips } from '../../components/workflow';
import { useNames } from './use-names';

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
}) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { centroNome } = useNames(companyId);

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Wallet />}
        title={`${stageLabel} — ${t('payables.empty.noItemsTitle')}`}
        action={
          <button className='btn empty-action' onClick={() => nav(`/${companyId}/payables/new`)}>
            <Plus /> {t('payables.new')}
          </button>
        }
      />
    );
  }

  return (
    <Panel title={`${stageLabel} · ${t('payables.count', { count: total })}`}>
      <table>
        <thead>
          <tr>
            <th>{t('payables.columns.dueDate')}</th>
            <th>{t('payables.columns.description')}</th>
            <th>{t('payables.columns.vendor')}</th>
            <th>{t('payables.columns.costCenter')}</th>
            <th style={{ textAlign: 'center' }}>{t('payables.columns.installments')}</th>
            <th style={{ textAlign: 'right' }}>{t('payables.columns.amount')}</th>
            <th>{t('payables.columns.status')}</th>
            {extraColumn && <th>{extraColumn.header}</th>}
          </tr>
        </thead>
        <tbody>
          {items.map((l) => (
            <tr key={l.payableId} className='row-click' onClick={() => nav(`/app/payables/${l.payableId}`)}>
              <td className='t-mono'>{fmtDate(l.firstDueDate)}</td>
              <td>{l.description}</td>
              <td>{l.counterpartyName || '—'}</td>
              <td>{centroNome[l.costCenterId] ?? '—'}</td>
              <td style={{ textAlign: 'center' }}>{l.installmentCount}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(l.totalAmount)}
              </td>
              <td onClick={(e) => e.stopPropagation()}>
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
                    {l.status === 'PendingAccounting' && l.hasFiscalDocument === false && (
                      <span className='badge-status badge-pendente' style={{ marginLeft: 6 }}>
                        {t('payables.tags.fiscalDocPending')}
                      </span>
                    )}
                  </>
                )}
              </td>
              {extraColumn && <td onClick={(e) => e.stopPropagation()}>{extraColumn.cell(l)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
