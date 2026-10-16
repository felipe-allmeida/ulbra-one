import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import type { CostCenterAssessmentRow } from '../../../api';
import { brlCents } from '../../../lib';
import { Panel } from '../../../ui';
import { ResponsiveTable, type Column } from '../../../components/ResponsiveTable';

/** "Por centro de custo": visão gerencial interna — NÃO é dimensão fiscal (nunca vai ao XML). */
export function ByCostCenterTable({
  rows,
  onDrill,
}: {
  rows: CostCenterAssessmentRow[];
  onDrill: (row: CostCenterAssessmentRow) => void;
}) {
  const { t } = useTranslation();

  // Ordem = a ordem já renderizada hoje. costCenter é a única `title` (botão que abre o drill-down).
  // withheld é o único `amount` (já em negrito hoje); count fica em `detail`.
  const columns: Column<CostCenterAssessmentRow>[] = [
    {
      key: 'costCenter',
      header: t('fiscal.obrigacoes.columns.costCenter'),
      role: 'title',
      cell: (r) => (
        <button type='button' className='linklike' onClick={() => onDrill(r)}>
          {r.name || r.code || '—'}
        </button>
      ),
    },
    {
      key: 'withheld',
      header: t('fiscal.obrigacoes.columns.withheld'),
      role: 'amount',
      align: 'right',
      cell: (r) => (
        <span className='amount-cell' style={{ fontWeight: 600 }}>
          {brlCents(r.withheldAmount)}
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
    <div style={{ marginTop: 16 }}>
      <Panel title={t('fiscal.obrigacoes.byCostCenter')}>
        <div className='help' style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '12px 16px 10px' }}>
          <AlertTriangle size={14} /> {t('fiscal.obrigacoes.costCenterNote')}
        </div>
        <ResponsiveTable rows={rows} columns={columns} rowKey={(r) => r.costCenterId} />
      </Panel>
    </div>
  );
}
