import { useTranslation } from 'react-i18next';
import { type BudgetAdjustment } from '../../api';
import { useCostCenters, useUnits } from '../../queries';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { Pager, Panel, SkeletonRows } from '../../ui';
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
          {loading && <SkeletonRows rows={5} cols={7} />}
          {!loading &&
            items.map((a) => (
              <tr key={a.id} className='row-click' onClick={() => openDetail(`/app/budget-adjustments/${a.id}`)}>
                <td className='t-mono'>#{a.code}</td>
                <td>
                  <BudgetAdjustmentTypeChip type={a.type} />
                </td>
                <td>{unitName(a.unitId)}</td>
                <td>{centroName(a.costCenterId)}</td>
                <td className='t-mono'>
                  {a.type === 'Transfer' && a.sourceCompetency ? `${fmtCompetency(a.sourceCompetency)} → ` : ''}
                  {fmtCompetency(a.competency)}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brl(a.value)}
                </td>
                <td>
                  <span style={{ display: 'inline-flex', gap: 6, alignItems: 'center' }}>
                    <BudgetAdjustmentStatusBadge s={a.status} />
                    <BudgetAdjustmentSlaChip a={a} />
                  </span>
                </td>
              </tr>
            ))}
        </tbody>
      </table>
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
