import { type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { HandCoins } from 'lucide-react';
import { type ReceivableListItem } from '../../api';
import { brl } from '../../lib';
import { Panel, Pager, EmptyState } from '../../ui';
import { fmtDate } from '../../datetime';
import { SlaChips } from '../../components/workflow';
import { useNames } from './use-names';

export function ReceivableTable({
  companyId,
  stageLabel,
  items,
  total,
  page,
  totalPages,
  onPage,
  extraColumn,
}: {
  companyId: string;
  stageLabel: string;
  items: ReceivableListItem[];
  total: number;
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
  extraColumn?: { header: string; cell: (item: ReceivableListItem) => ReactNode };
}) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { centroNome } = useNames(companyId);

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<HandCoins />}
        title={`${stageLabel} — ${t('receivables.empty.noItemsTitle')}`}
        sub={t('receivables.empty.noItemsSub')}
      />
    );
  }

  return (
    <Panel title={`${stageLabel} · ${t('receivables.count', { count: total })}`}>
      <table>
        <thead>
          <tr>
            <th>{t('receivables.columns.dueDate')}</th>
            <th>{t('receivables.columns.description')}</th>
            <th>{t('receivables.columns.payer')}</th>
            <th>{t('receivables.columns.costCenter')}</th>
            <th style={{ textAlign: 'right' }}>{t('receivables.columns.amount')}</th>
            <th>{t('receivables.columns.status')}</th>
            {extraColumn && <th>{extraColumn.header}</th>}
          </tr>
        </thead>
        <tbody>
          {items.map((l) => (
            <tr key={l.installmentId} className='row-click' onClick={() => nav(`/app/receivables/${l.receivableId}`)}>
              <td className='t-mono'>{fmtDate(l.dueDate)}</td>
              <td>{l.description}</td>
              <td>{l.payerName || '—'}</td>
              <td>{centroNome[l.costCenterId] ?? '—'}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(l.amount)}
              </td>
              <td onClick={(e) => e.stopPropagation()}>
                <SlaChips stageLabel={stageLabel} dueDate={l.dueDate} />
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
