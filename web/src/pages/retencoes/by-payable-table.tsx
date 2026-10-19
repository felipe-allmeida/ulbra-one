import { useTranslation } from 'react-i18next';
import type { WithholdingPayableRow } from '../../api';
import { brlCents } from '../../lib';
import { Panel } from '../../ui';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';

/** "By payable": one row per payable with withholding; clicking the name opens the breakdown (onSelect). */
export function ByPayableTable({
  rows,
  onSelect,
}: {
  rows: WithholdingPayableRow[];
  onSelect: (row: WithholdingPayableRow) => void;
}) {
  const { t } = useTranslation();

  // Ordem = a ordem já renderizada hoje. supplier é a única `title` (o botão que abre o detalhe, não um
  // clique de linha — por isso não há onRowClick aqui). document é `meta`. gross/realized/foreseen são
  // valores monetários secundários → `detail`; net é o único `amount` (o número final, já em negrito hoje).
  const columns: Column<WithholdingPayableRow>[] = [
    {
      key: 'supplier',
      header: t('accounting.withholdings.columns.supplier'),
      role: 'title',
      cell: (p) => (
        <button type='button' className='linklike' onClick={() => onSelect(p)}>
          {p.counterpartyName}
        </button>
      ),
    },
    {
      key: 'document',
      header: t('accounting.withholdings.columns.document'),
      role: 'meta',
      cell: (p) => <span className='t-mono'>{p.documentNumber ?? '—'}</span>,
    },
    {
      key: 'status',
      header: t('accounting.withholdings.columns.status'),
      role: 'status',
      cell: (p) => <span className='badge-status'>{t(`payables.status.${p.status}`)}</span>,
    },
    {
      key: 'gross',
      header: t('accounting.withholdings.columns.gross'),
      role: 'detail',
      align: 'right',
      cell: (p) => <span className='amount-cell'>{brlCents(p.totalAmount)}</span>,
    },
    {
      key: 'realized',
      header: t('accounting.withholdings.columns.realized'),
      role: 'detail',
      align: 'right',
      cell: (p) => <span className='amount-cell'>{brlCents(p.realizedAmount)}</span>,
    },
    {
      key: 'foreseen',
      header: t('accounting.withholdings.columns.foreseen'),
      role: 'detail',
      align: 'right',
      cell: (p) => (
        <span
          className='amount-cell'
          style={{ color: p.foreseenAmount > 0 ? 'var(--warning-foreground, inherit)' : 'var(--muted-foreground)' }}
        >
          {p.foreseenAmount > 0 ? brlCents(p.foreseenAmount) : '—'}
        </span>
      ),
    },
    {
      key: 'net',
      header: t('accounting.withholdings.columns.net'),
      role: 'amount',
      align: 'right',
      cell: (p) => (
        <span className='amount-cell' style={{ fontWeight: 600 }}>
          {brlCents(p.netTotal)}
        </span>
      ),
    },
  ];

  return (
    <div style={{ marginTop: 16 }}>
      <Panel title={t('accounting.withholdings.byPayable')}>
        <ResponsiveTable rows={rows} columns={columns} rowKey={(p) => p.id} />
      </Panel>
    </div>
  );
}
