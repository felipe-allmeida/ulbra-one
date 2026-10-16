import { useTranslation } from 'react-i18next';
import { type BudgetAdjustment } from '../../api';
import { useCostCenters, useUnits } from '../../queries';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { Pager, Panel, SkeletonRows } from '../../ui';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { fmtCompetency } from '../requisitions/competency';
import { BudgetAdjustmentSlaChip, BudgetAdjustmentStatusBadge, BudgetAdjustmentTypeChip } from './badges';

/** Budget-adjustments table (Code | Type | Unit | Cost Center | Competency | Value | Status). */
export function BudgetAdjustmentTable({
  companyId,
  items,
  total,
  page,
  totalPages,
  onPage,
  loading,
}: {
  companyId: string;
  items: BudgetAdjustment[];
  total: number;
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
  /** Enquanto o GET carrega: mostra skeleton rows no corpo da tabela. */
  loading?: boolean;
}) {
  const { t } = useTranslation();
  const openDetail = useOriginNav();
  const units = useUnits(companyId).data ?? [];
  const centros = useCostCenters(companyId).data ?? [];
  const unitName = (id: string) => units.find((u) => u.id === id)?.acronym ?? '—';
  const centroName = (id: string) => centros.find((c) => c.id === id)?.name ?? '—';

  if (loading) {
    return (
      <Panel title={`${t('budgetAdjustments.listTitle')} · ${t('budgetAdjustments.count', { count: total })}`}>
        <table>
          <thead>
            <tr>
              <th>{t('budgetAdjustments.columns.code')}</th>
              <th>{t('budgetAdjustments.columns.type')}</th>
              <th>{t('budgetAdjustments.columns.unit')}</th>
              <th>{t('budgetAdjustments.columns.costCenter')}</th>
              <th>{t('budgetAdjustments.columns.competency')}</th>
              <th style={{ textAlign: 'right' }}>{t('budgetAdjustments.columns.value')}</th>
              <th>{t('budgetAdjustments.columns.status')}</th>
            </tr>
          </thead>
          <tbody>
            <SkeletonRows rows={5} cols={7} />
          </tbody>
        </table>
        <Pager page={page} totalPages={totalPages} onPage={onPage} />
      </Panel>
    );
  }

  // Ordem = a ordem já renderizada hoje. código é a única `title` (identificador curto); unidade/natureza
  // orçamentária são os `meta` (2 é o teto — ver cardinalidade em ResponsiveTable.tsx); competência é
  // conteúdo composto (pode levar a seta "origem → destino" numa transferência) → `detail`. valor é o
  // único `amount`; tipo (chip) e status (selo + chip de SLA) são ambos badges → `status` (empilham).
  const columns: Column<BudgetAdjustment>[] = [
    {
      key: 'code',
      header: t('budgetAdjustments.columns.code'),
      role: 'title',
      cell: (a) => <span className='t-mono'>#{a.code}</span>,
    },
    {
      key: 'type',
      header: t('budgetAdjustments.columns.type'),
      role: 'status',
      cell: (a) => <BudgetAdjustmentTypeChip type={a.type} />,
    },
    { key: 'unit', header: t('budgetAdjustments.columns.unit'), role: 'meta', cell: (a) => unitName(a.unitId) },
    {
      key: 'costCenter',
      header: t('budgetAdjustments.columns.costCenter'),
      role: 'meta',
      cell: (a) => centroName(a.costCenterId),
    },
    {
      key: 'competency',
      header: t('budgetAdjustments.columns.competency'),
      role: 'detail',
      cell: (a) => (
        <span className='t-mono'>
          {a.type === 'Transfer' && a.sourceCompetency ? `${fmtCompetency(a.sourceCompetency)} → ` : ''}
          {fmtCompetency(a.competency)}
        </span>
      ),
    },
    {
      key: 'value',
      header: t('budgetAdjustments.columns.value'),
      role: 'amount',
      align: 'right',
      cell: (a) => <span className='amount-cell'>{brl(a.value)}</span>,
    },
    {
      key: 'status',
      header: t('budgetAdjustments.columns.status'),
      role: 'status',
      cell: (a) => (
        <span style={{ display: 'inline-flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
          <BudgetAdjustmentStatusBadge s={a.status} />
          <BudgetAdjustmentSlaChip a={a} />
        </span>
      ),
    },
  ];

  return (
    <Panel title={`${t('budgetAdjustments.listTitle')} · ${t('budgetAdjustments.count', { count: total })}`}>
      <ResponsiveTable
        rows={items}
        columns={columns}
        rowKey={(a) => a.id}
        onRowClick={(a) => openDetail(`/app/budget-adjustments/${a.id}`)}
      />
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
