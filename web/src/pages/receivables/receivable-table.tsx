import { type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { HandCoins, Plus } from 'lucide-react';
import { type ReceivableStageItem } from '../../api';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { P, usePermissions } from '../../permissions';
import { EmptyState, onRowClick, Pager, Panel, SkeletonRows } from '../../ui';
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
  loading,
}: {
  companyId: string;
  stageLabel: string;
  items: ReceivableStageItem[];
  total: number;
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
  extraColumn?: { header: string; cell: (item: ReceivableStageItem) => ReactNode };
  /** Enquanto o GET carrega: mostra skeleton rows em vez de piscar o estado vazio. */
  loading?: boolean;
}) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const openDetail = useOriginNav();
  const { can } = usePermissions();
  const { centroNome } = useNames(companyId);

  const colCount = 7 + (extraColumn ? 1 : 0);

  const head = (
    <thead>
      <tr>
        <th>{t('receivables.columns.dueDate')}</th>
        <th>{t('receivables.columns.description')}</th>
        <th>{t('receivables.columns.payer')}</th>
        <th>{t('receivables.columns.costCenter')}</th>
        <th style={{ textAlign: 'center' }}>{t('receivables.columns.installments')}</th>
        <th style={{ textAlign: 'right' }}>{t('receivables.columns.amount')}</th>
        <th>{t('receivables.columns.status')}</th>
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
    return (
      <EmptyState
        icon={<HandCoins />}
        title={`${stageLabel} — ${t('receivables.empty.noItemsTitle')}`}
        action={
          can(P.receivablesWrite) ? (
            <button className='btn empty-action' onClick={() => nav(`/${companyId}/receivables/new`)}>
              <Plus /> {t('receivables.new')}
            </button>
          ) : undefined
        }
      />
    );
  }

  return (
    <Panel title={`${stageLabel} · ${t('receivables.count', { count: total })}`}>
      <table>
        {head}
        <tbody>
          {items.map((l) => (
            <tr
              key={l.receivableId}
              className='row-click'
              onClick={onRowClick(() => openDetail(`/app/receivables/${l.receivableId}`))}
            >
              <td className='t-mono'>{fmtDate(l.firstDueDate)}</td>
              <td>{l.description}</td>
              <td>{l.payerName || '—'}</td>
              <td>{centroNome[l.costCenterId] ?? '—'}</td>
              <td style={{ textAlign: 'center' }}>{l.installmentCount}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(l.totalAmount)}
              </td>
              <td>
                <SlaChips stageLabel={stageLabel} dueDate={l.firstDueDate} />
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
