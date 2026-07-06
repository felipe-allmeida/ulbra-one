import { useTranslation } from 'react-i18next';
import type { WithholdingByTaxRow } from '../../api';
import { brlCents } from '../../lib';
import { Panel } from '../../ui';

/** "By tax": withholdings aggregated per tax type, with a footer total. */
export function ByTaxTable({ rows, withheldTotal }: { rows: WithholdingByTaxRow[]; withheldTotal: number }) {
  const { t } = useTranslation();
  return (
    <Panel title={t('accounting.withholdings.byTax')}>
      <table>
        <thead>
          <tr>
            <th>{t('accounting.withholdings.columns.tax')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.base')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.amount')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.count')}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.taxType}>
              <td>{t(`payables.withholding.taxType.${r.taxType}`)}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(r.baseAmount)}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(r.amount)}
              </td>
              <td style={{ textAlign: 'right' }}>{r.payableCount}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td style={{ fontWeight: 600 }}>{t('accounting.withholdings.total')}</td>
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
