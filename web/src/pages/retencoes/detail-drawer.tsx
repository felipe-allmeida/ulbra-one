import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { WithholdingPayableRow } from '../../api';
import { brlCents } from '../../lib';
import { useOriginState } from '../../nav';
import { Drawer } from '../../ui';

/** Side drawer with a payable's per-tax withholding breakdown (data already comes in the report GET). */
export function WithholdingDetailDrawer({ row, onClose }: { row: WithholdingPayableRow | null; onClose: () => void }) {
  const { t } = useTranslation();
  const originState = useOriginState();
  if (!row) return null;

  return (
    <Drawer open onClose={onClose} title={t('accounting.withholdings.detail.title')} sub={row.counterpartyName}>
      {/* Gross and Net side by side; Retido and A reter split the withheld total. */}
      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)', gap: 12, marginBottom: 16 }}>
        <div className='info-card'>
          <div className='info-label'>{t('accounting.withholdings.gross')}</div>
          <div className='info-value'>{brlCents(row.totalAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('accounting.withholdings.net')}</div>
          <div className='info-value'>{brlCents(row.netTotal)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('accounting.withholdings.realized')}</div>
          <div className='info-value'>{brlCents(row.realizedAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('accounting.withholdings.foreseen')}</div>
          <div className='info-value'>{row.foreseenAmount > 0 ? brlCents(row.foreseenAmount) : '—'}</div>
        </div>
      </div>

      {row.lines.length > 0 ? (
        <div className='drawer-table-scroll'>
          <table>
            <thead>
              <tr>
                <th>{t('accounting.withholdings.detail.columns.tax')}</th>
                <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.detail.columns.base')}</th>
                <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.detail.columns.rate')}</th>
                <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.detail.columns.realized')}</th>
                <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.detail.columns.foreseen')}</th>
              </tr>
            </thead>
            <tbody>
              {row.lines.map((l) => (
                <tr key={l.taxType}>
                  <td>
                    {t(`payables.withholding.taxType.${l.taxType}`)}
                    {l.timing === 'OnPayment' && (
                      <span className='ret-row-sub'> · {t('payables.withholding.moment.onPayment')}</span>
                    )}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(l.baseAmount)}
                  </td>
                  <td style={{ textAlign: 'right' }}>{l.rate != null ? `${(l.rate * 100).toFixed(2)}%` : '—'}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(l.realizedAmount)}
                  </td>
                  <td
                    className='amount-cell'
                    style={{
                      textAlign: 'right',
                      color: l.foreseenAmount > 0 ? 'var(--warning-foreground, inherit)' : 'var(--muted-foreground)',
                    }}
                  >
                    {l.foreseenAmount > 0 ? brlCents(l.foreseenAmount) : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <td style={{ fontWeight: 600 }}>{t('accounting.withholdings.total')}</td>
                <td />
                <td />
                <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                  {brlCents(row.realizedAmount)}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                  {row.foreseenAmount > 0 ? brlCents(row.foreseenAmount) : '—'}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      ) : (
        <div className='help'>{t('accounting.withholdings.detail.empty')}</div>
      )}

      <div style={{ marginTop: 16 }}>
        <Link className='linklike' to={`/app/payables/${row.id}`} state={originState}>
          {t('accounting.withholdings.detail.openPayable')}
        </Link>
      </div>
    </Drawer>
  );
}
