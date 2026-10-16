import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Landmark } from 'lucide-react';
import { type BalanceSheetLine } from '../api';
import { useApp } from '../store';
import { useBalanceSheet } from '../queries';
import { endOfCurrentMonth } from '../datetime';
import { brlCents } from '../lib';
import { EmptyState, Input, PageHeader, Panel, ToolbarField, LoadingState } from '../ui';

/** One side of the balance sheet (a column): a titled list of account lines + a total. */
function BalanceColumn({
  title,
  lines,
  total,
  totalLabel,
}: {
  title: string;
  lines: BalanceSheetLine[];
  total: number;
  totalLabel: string;
}) {
  return (
    <div className='balance-col'>
      <Panel title={title}>
        <table>
          <tbody>
            {lines.map((l, i) => (
              <tr key={l.ledgerAccountId ?? `synthetic-${i}`}>
                <td>
                  {l.accountCode && <span className='t-mono'>{l.accountCode} </span>}
                  {l.accountName}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brlCents(l.balance)}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td style={{ fontWeight: 700 }}>{totalLabel}</td>
              <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                {brlCents(total)}
              </td>
            </tr>
          </tfoot>
        </table>
      </Panel>
    </div>
  );
}

/** Balanço patrimonial. The empresa/consolidado scope comes from the topbar selector (`useApp`), not
 *  from a page filter — the page only picks the reference date. */
export function ContabilBalancoPage() {
  const { t } = useTranslation();
  const { scopeCompanyId, scopeReady } = useApp();
  const [asOf, setAsOf] = useState(endOfCurrentMonth);

  const query = useBalanceSheet({ companyId: scopeCompanyId, asOf: asOf || undefined }, { enabled: scopeReady });
  const bs = query.data;
  const hasData = bs && (bs.assets.length > 0 || bs.liabilities.length > 0 || bs.equity.length > 0);
  const totalLiabEquity = (bs?.totalLiabilities ?? 0) + (bs?.totalEquity ?? 0);

  return (
    <>
      <PageHeader title={t('accounting.balance.title')} sub={t('accounting.balance.subtitle')}>
        <ToolbarField label={t('accounting.balance.asOf')}>
          <Input type='date' value={asOf} onChange={(e) => setAsOf(e.target.value)} />
        </ToolbarField>
      </PageHeader>

      {!scopeReady || query.isLoading ? (
        <LoadingState />
      ) : !hasData ? (
        <EmptyState
          icon={<Landmark />}
          title={t('accounting.balance.empty.title')}
          sub={t('accounting.balance.empty.sub')}
        />
      ) : (
        <>
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
            <span className={`badge-status ${bs.isBalanced ? 'badge-aprovado' : 'badge-rejeitado'}`}>
              {bs.isBalanced ? t('accounting.balance.balanced') : t('accounting.balance.unbalanced')}
            </span>
          </div>
          <div className='balance-grid'>
            <BalanceColumn
              title={t('accounting.balance.assets')}
              lines={bs.assets}
              total={bs.totalAssets}
              totalLabel={t('accounting.balance.totalAssets')}
            />
            <div className='balance-col'>
              <Panel title={`${t('accounting.balance.liabilities')} + ${t('accounting.balance.equity')}`}>
                <table>
                  <tbody>
                    <tr className='dre-section'>
                      <td colSpan={2} style={{ fontWeight: 600 }}>
                        {t('accounting.balance.liabilities')}
                      </td>
                    </tr>
                    {bs.liabilities.map((l, i) => (
                      <tr key={l.ledgerAccountId ?? `liab-${i}`}>
                        <td>
                          {l.accountCode && <span className='t-mono'>{l.accountCode} </span>}
                          {l.accountName}
                        </td>
                        <td className='amount-cell' style={{ textAlign: 'right' }}>
                          {brlCents(l.balance)}
                        </td>
                      </tr>
                    ))}
                    <tr className='dre-section'>
                      <td colSpan={2} style={{ fontWeight: 600 }}>
                        {t('accounting.balance.equity')}
                      </td>
                    </tr>
                    {bs.equity.map((l, i) => (
                      <tr key={l.ledgerAccountId ?? `eq-${i}`}>
                        <td>
                          {l.accountCode && <span className='t-mono'>{l.accountCode} </span>}
                          {l.accountName || t('accounting.balance.periodResult')}
                        </td>
                        <td className='amount-cell' style={{ textAlign: 'right' }}>
                          {brlCents(l.balance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr>
                      <td style={{ fontWeight: 700 }}>{t('accounting.balance.totalLiabilitiesEquity')}</td>
                      <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                        {brlCents(totalLiabEquity)}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </Panel>
            </div>
          </div>
        </>
      )}
    </>
  );
}
