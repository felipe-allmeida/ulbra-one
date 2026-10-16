import { useTranslation } from 'react-i18next';
import type { WithholdingByTaxRow } from '../../api';
import { brlCents } from '../../lib';
import { Panel } from '../../ui';

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
  return (
    <Panel title={t('accounting.withholdings.byTax')}>
      <table>
        <thead>
          <tr>
            <th>{t('accounting.withholdings.columns.tax')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.base')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.realized')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.foreseen')}</th>
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
                {brlCents(r.realizedAmount)}
              </td>
              <td
                className='amount-cell'
                style={{
                  textAlign: 'right',
                  color: r.foreseenAmount > 0 ? 'var(--warning-foreground, inherit)' : 'var(--muted-foreground)',
                }}
              >
                {r.foreseenAmount > 0 ? brlCents(r.foreseenAmount) : '—'}
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
              {brlCents(realizedTotal)}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {foreseenTotal > 0 ? brlCents(foreseenTotal) : '—'}
            </td>
            <td />
          </tr>
        </tfoot>
      </table>
    </Panel>
  );
}
