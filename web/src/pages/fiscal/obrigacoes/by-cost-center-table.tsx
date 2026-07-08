import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import type { CostCenterAssessmentRow } from '../../../api';
import { brlCents } from '../../../lib';
import { Panel } from '../../../ui';

/** "Por centro de custo": visão gerencial interna — NÃO é dimensão fiscal (nunca vai ao XML). */
export function ByCostCenterTable({
  rows,
  onDrill,
}: {
  rows: CostCenterAssessmentRow[];
  onDrill: (row: CostCenterAssessmentRow) => void;
}) {
  const { t } = useTranslation();
  return (
    <div style={{ marginTop: 16 }}>
      <Panel title={t('fiscal.obrigacoes.byCostCenter')}>
        <div className='help' style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 10 }}>
          <AlertTriangle size={14} /> {t('fiscal.obrigacoes.costCenterNote')}
        </div>
        <table>
          <thead>
            <tr>
              <th>{t('fiscal.obrigacoes.columns.costCenter')}</th>
              <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.withheld')}</th>
              <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.count')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.costCenterId}>
                <td>
                  <button type='button' className='linklike' onClick={() => onDrill(r)}>
                    {r.name || r.code || '—'}
                  </button>
                </td>
                <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                  {brlCents(r.withheldAmount)}
                </td>
                <td style={{ textAlign: 'right' }}>{r.payableCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </div>
  );
}
