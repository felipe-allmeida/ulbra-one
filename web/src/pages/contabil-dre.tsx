import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { LineChart } from 'lucide-react';
import { type IncomeStatementLine } from '../api';
import { useIncomeStatement } from '../queries';
import { brlCents } from '../lib';
import { EmptyState, Input, PageHeader, Panel, ToolbarField } from '../ui';
import { ConsolidatedPicker } from './contabil-shared';

/** A group of statement lines under a section header, with a subtotal. */
function StatementSection({
  title,
  lines,
  total,
  sign,
}: {
  title: string;
  lines: IncomeStatementLine[];
  total: number;
  sign: 1 | -1;
}) {
  return (
    <>
      <tr className='dre-section'>
        <td colSpan={2} style={{ fontWeight: 600 }}>
          {title}
        </td>
      </tr>
      {lines.map((l) => (
        <tr key={l.ledgerAccountId}>
          <td style={{ paddingLeft: 24 }}>
            <span className='t-mono'>{l.accountCode}</span> {l.accountName}
          </td>
          <td className='amount-cell' style={{ textAlign: 'right' }}>
            {brlCents(sign * l.amount)}
          </td>
        </tr>
      ))}
      <tr className='dre-subtotal'>
        <td style={{ fontWeight: 600 }}>{title}</td>
        <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
          {brlCents(sign * total)}
        </td>
      </tr>
    </>
  );
}

export function ContabilDrePage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const query = useIncomeStatement({ companyId: companyId || undefined, from: from || undefined, to: to || undefined });
  const dre = query.data;
  const hasData = dre && (dre.revenues.length > 0 || dre.expenses.length > 0);

  return (
    <>
      <PageHeader title={t('accounting.income.title')} sub={t('accounting.income.subtitle')}>
        <ConsolidatedPicker value={companyId} onChange={setCompanyId} />
        <ToolbarField label={t('accounting.journal.filters.from')}>
          <Input type='date' value={from} onChange={(e) => setFrom(e.target.value)} />
        </ToolbarField>
        <ToolbarField label={t('accounting.journal.filters.to')}>
          <Input type='date' value={to} onChange={(e) => setTo(e.target.value)} />
        </ToolbarField>
      </PageHeader>

      {query.isLoading ? (
        <div className='help'>{t('accounting.journal.loading')}</div>
      ) : !hasData ? (
        <EmptyState
          icon={<LineChart />}
          title={t('accounting.income.empty.title')}
          sub={t('accounting.income.empty.sub')}
        />
      ) : (
        <Panel title={t('accounting.income.title')}>
          <table className='dre-table'>
            <tbody>
              <StatementSection
                title={t('accounting.income.revenues')}
                lines={dre.revenues}
                total={dre.totalRevenue}
                sign={1}
              />
              <StatementSection
                title={t('accounting.income.expenses')}
                lines={dre.expenses}
                total={dre.totalExpense}
                sign={-1}
              />
              <tr className='dre-result'>
                <td style={{ fontWeight: 700 }}>
                  {dre.result >= 0 ? t('accounting.income.profit') : t('accounting.income.loss')}
                </td>
                <td
                  className='amount-cell'
                  style={{
                    textAlign: 'right',
                    fontWeight: 700,
                    color: dre.result >= 0 ? 'var(--success)' : 'var(--danger)',
                  }}
                >
                  {brlCents(dre.result)}
                </td>
              </tr>
            </tbody>
          </table>
        </Panel>
      )}
    </>
  );
}
