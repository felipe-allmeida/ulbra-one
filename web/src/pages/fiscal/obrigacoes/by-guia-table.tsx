import { useTranslation } from 'react-i18next';
import type { RetentionGroupRow } from '../../../api';
import { brlCents } from '../../../lib';
import { Panel } from '../../../ui';

/** "Por guia": retenções agregadas por grupo de recolhimento (IRRF / CSRF / INSS / ISS). */
export function GuiaTable({ rows, withheldTotal }: { rows: RetentionGroupRow[]; withheldTotal: number }) {
  const { t } = useTranslation();
  return (
    <Panel title={t('fiscal.obrigacoes.byGuia')}>
      <table>
        <thead>
          <tr>
            <th>{t('fiscal.obrigacoes.columns.guia')}</th>
            <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.base')}</th>
            <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.withheld')}</th>
            <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.count')}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.group}>
              <td>{t(`fiscal.obrigacoes.guia.${r.group}`)}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(r.baseAmount)}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(r.withheldAmount)}
              </td>
              <td style={{ textAlign: 'right' }}>{r.payableCount}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td style={{ fontWeight: 600 }}>{t('fiscal.obrigacoes.total')}</td>
            <td />
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(withheldTotal)}
            </td>
            <td />
          </tr>
        </tfoot>
      </table>
    </Panel>
  );
}
