import { useTranslation } from 'react-i18next';
import type { WithholdingPayableRow } from '../../api';
import { brlCents } from '../../lib';
import { Panel } from '../../ui';

/** "Por título": uma linha por payable com retenção; clicar no nome abre o detalhamento (onSelect). */
export function ByPayableTable({
  rows,
  onSelect,
}: {
  rows: WithholdingPayableRow[];
  onSelect: (row: WithholdingPayableRow) => void;
}) {
  const { t } = useTranslation();
  return (
    <div style={{ marginTop: 16 }}>
      <Panel title={t('accounting.withholdings.byPayable')}>
        <table>
          <thead>
            <tr>
              <th>{t('accounting.withholdings.columns.supplier')}</th>
              <th>{t('accounting.withholdings.columns.document')}</th>
              <th>{t('accounting.withholdings.columns.status')}</th>
              <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.gross')}</th>
              <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.amount')}</th>
              <th style={{ textAlign: 'right' }}>{t('accounting.withholdings.columns.net')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((p) => (
              <tr key={p.id}>
                <td>
                  <button type='button' className='linklike' onClick={() => onSelect(p)}>
                    {p.counterpartyName}
                  </button>
                </td>
                <td className='t-mono'>{p.documentNumber ?? '—'}</td>
                <td>
                  <span className='badge-status'>{t(`payables.status.${p.status}`)}</span>
                </td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brlCents(p.totalAmount)}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brlCents(p.totalWithheld)}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                  {brlCents(p.netTotal)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </div>
  );
}
