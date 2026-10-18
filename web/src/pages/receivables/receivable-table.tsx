import { type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { HandCoins, Plus } from 'lucide-react';
import { type ReceivableStageItem } from '../../api';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { P, usePermissions } from '../../permissions';
import { EmptyState, Pager, Panel, SkeletonRows } from '../../ui';
import { fmtDate } from '../../datetime';
import { SlaChips } from '../../components/workflow';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
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

  // Ordem = a ordem já renderizada hoje (vencimento antes de descrição) — preserva o desktop pixel a pixel.
  // Mesma regra de papel do payable-table.tsx: description é a única `title`; vencimento/pagador são `meta`;
  // total é o único `amount`; centro de custo e parcelas são `detail`.
  const columns: Column<ReceivableStageItem>[] = [
    {
      key: 'dueDate',
      header: t('receivables.columns.dueDate'),
      role: 'meta',
      cell: (l) => <span className='t-mono'>{fmtDate(l.firstDueDate)}</span>,
    },
    { key: 'description', header: t('receivables.columns.description'), role: 'title', cell: (l) => l.description },
    { key: 'payer', header: t('receivables.columns.payer'), role: 'meta', cell: (l) => l.payerName || '—' },
    {
      key: 'costCenter',
      header: t('receivables.columns.costCenter'),
      role: 'detail',
      cell: (l) => centroNome[l.costCenterId] ?? '—',
    },
    {
      key: 'installments',
      header: t('receivables.columns.installments'),
      role: 'detail',
      align: 'center',
      cell: (l) => l.installmentCount,
    },
    {
      key: 'total',
      header: t('receivables.columns.amount'),
      role: 'amount',
      align: 'right',
      cell: (l) => <span className='amount-cell'>{brl(l.totalAmount)}</span>,
    },
    {
      key: 'status',
      header: t('receivables.columns.status'),
      role: 'status',
      cell: (l) => <SlaChips stageLabel={stageLabel} dueDate={l.firstDueDate} />,
    },
    ...(extraColumn
      ? [
          {
            key: 'extra',
            header: extraColumn.header,
            role: 'detail' as const,
            cell: (l: ReceivableStageItem) => extraColumn.cell(l),
          },
        ]
      : []),
  ];

  return (
    <Panel title={`${stageLabel} · ${t('receivables.count', { count: total })}`}>
      <ResponsiveTable
        rows={items}
        columns={columns}
        rowKey={(l) => l.receivableId}
        onRowClick={(l) => openDetail(`/app/receivables/${l.receivableId}`)}
      />
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
