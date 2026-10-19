import { useTranslation } from 'react-i18next';
import type { WithholdingByTaxRow } from '../../api';
import { brlCents } from '../../lib';
import { Panel } from '../../ui';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';

/** "By tax": withholdings aggregated per tax type, split into retido (realizado) and a reter (previsto). */
export function ByTaxTable({
  rows,
  realizedTotal,
  foreseenTotal,
}: {
  rows: WithholdingByTaxRow[];
  realizedTotal: number;
  foreseenTotal: number;
}) {
  const { t } = useTranslation();

  // Ordem = a ordem já renderizada hoje. tax é a única `title`; realized é o único `amount` (o retido já
  // efetivado — o número que fecha a história da linha); base/foreseen/count são `detail`.
  // realizedTotal/foreseenTotal vêm de report.totals (agregado da API), não da soma de `rows` — o relatório
  // pode ter filtro parcial; ler os props preserva esse comportamento (mesmo raciocínio do balancete).
  const columns: Column<WithholdingByTaxRow>[] = [
    {
      key: 'tax',
      header: t('accounting.withholdings.columns.tax'),
      role: 'title',
      cell: (r) => t(`payables.withholding.taxType.${r.taxType}`),
    },
    {
      key: 'base',
      header: t('accounting.withholdings.columns.base'),
      role: 'detail',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.baseAmount)}</span>,
    },
    {
      key: 'realized',
      header: t('accounting.withholdings.columns.realized'),
      role: 'amount',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.realizedAmount)}</span>,
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {brlCents(realizedTotal)}
        </span>
      ),
    },
    {
      key: 'foreseen',
      header: t('accounting.withholdings.columns.foreseen'),
      role: 'detail',
      align: 'right',
      cell: (r) => (
        <span
          className='amount-cell'
          style={{ color: r.foreseenAmount > 0 ? 'var(--warning-foreground, inherit)' : 'var(--muted-foreground)' }}
        >
          {r.foreseenAmount > 0 ? brlCents(r.foreseenAmount) : '—'}
        </span>
      ),
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {foreseenTotal > 0 ? brlCents(foreseenTotal) : '—'}
        </span>
      ),
    },
    {
      key: 'count',
      header: t('accounting.withholdings.columns.count'),
      role: 'detail',
      align: 'right',
      cell: (r) => r.payableCount,
    },
  ];

  return (
    <Panel title={t('accounting.withholdings.byTax')}>
      <ResponsiveTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.taxType}
        footLabel={<span style={{ fontWeight: 600 }}>{t('accounting.withholdings.total')}</span>}
      />
    </Panel>
  );
}
