import { useTranslation } from 'react-i18next';
import type { RetentionGroupRow } from '../../../api';
import { brlCents } from '../../../lib';
import { Panel } from '../../../ui';
import { ResponsiveTable, type Column } from '../../../components/ResponsiveTable';

/** "Por guia": retenções agregadas por grupo de recolhimento (IRRF / CSRF / INSS / ISS). */
export function GuiaTable({ rows, withheldTotal }: { rows: RetentionGroupRow[]; withheldTotal: number }) {
  const { t } = useTranslation();

  // Ordem = a ordem já renderizada hoje. guia é a única `title`; withheld é o único `amount`; base/count
  // são `detail`. withheldTotal vem de data.totals.withheldTotal (agregado da API) — não é a soma de `rows`
  // aqui (o mesmo raciocínio do balancete: ler o campo do relatório, não somar as linhas visíveis).
  const columns: Column<RetentionGroupRow>[] = [
    {
      key: 'guia',
      header: t('fiscal.obrigacoes.columns.guia'),
      role: 'title',
      cell: (r) => t(`fiscal.obrigacoes.guia.${r.group}`),
    },
    {
      key: 'base',
      header: t('fiscal.obrigacoes.columns.base'),
      role: 'detail',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.baseAmount)}</span>,
    },
    {
      key: 'withheld',
      header: t('fiscal.obrigacoes.columns.withheld'),
      role: 'amount',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.withheldAmount)}</span>,
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {brlCents(withheldTotal)}
        </span>
      ),
    },
    {
      key: 'count',
      header: t('fiscal.obrigacoes.columns.count'),
      role: 'detail',
      align: 'right',
      cell: (r) => r.payableCount,
    },
  ];

  return (
    <Panel title={t('fiscal.obrigacoes.byGuia')}>
      <ResponsiveTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.group}
        footLabel={<span style={{ fontWeight: 600 }}>{t('fiscal.obrigacoes.total')}</span>}
      />
    </Panel>
  );
}
