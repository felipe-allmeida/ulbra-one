import { Fragment, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { LineChart, Lock } from 'lucide-react';
import { api, type DreLine } from '../api';
import { useApp } from '../store';
import { useIncomeStatement, useYearEndClosing } from '../queries';
import { startOfCurrentMonth, endOfCurrentMonth } from '../datetime';
import { brlCents } from '../lib';
import { downloadWithAuth } from '../download';
import { getErrorMessage } from '../apiError';
import { useToast } from '../toast';
import { P, usePermissions } from '../permissions';
import { Button, EmptyState, Field, Input, Modal, Panel, LoadingState } from '../ui';
import { ReportHead, ReportToolbar } from './contabil-shared';

/** Structured DRE (income statement) as a cascade: per-account sections grouped by DRE line, interleaved with
 *  the running subtotals (Receita Líquida → Lucro Bruto → Resultado Operacional → LAIR → Lucro Líquido).
 *  Amounts are signed contributions to the result (revenues positive; costs/expenses/deductions negative).
 *  The empresa/consolidado scope comes from the topbar selector (`useApp`), not from a page filter. */
export function ContabilDrePage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { can } = usePermissions();
  const canClose = can(P.accountingPost);
  const { scopeCompanyId, scopeReady } = useApp();
  const [from, setFrom] = useState(startOfCurrentMonth);
  const [to, setTo] = useState(endOfCurrentMonth);
  const [costCenterId, setCostCenterId] = useState('');
  const [unitId, setUnitId] = useState('');
  const [closingOpen, setClosingOpen] = useState(false);
  const [closingYear, setClosingYear] = useState('');

  // Guard the invalid competence range client-side (mirrors the Balancete): the endpoint returns 400 for
  // from > to, which would otherwise surface as the generic empty state. Skip the query and show a hint.
  const invalidRange = !!(from && to && from > to);
  const query = useIncomeStatement(
    {
      companyId: scopeCompanyId,
      from: from || undefined,
      to: to || undefined,
      costCenterId: costCenterId || undefined,
      unitId: unitId || undefined,
    },
    { enabled: !invalidRange && scopeReady },
  );
  const dre = query.data;
  const hasData = dre && dre.sections.length > 0;

  const closing = useYearEndClosing({
    onSuccess: (r) => {
      toast.success(
        r.alreadyClosed
          ? t('accounting.yearEndClosing.alreadyClosed', { year: r.fiscalYear })
          : t('accounting.yearEndClosing.done', { year: r.fiscalYear, value: brlCents(r.netIncome) }),
      );
      setClosingOpen(false);
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  // Year-end closing: only for a specific company (per-company), gated to accounting:post.
  const openClosing = () => {
    setClosingYear((to || from).slice(0, 4) || String(new Date().getFullYear()));
    setClosingOpen(true);
  };
  const closeAction = canClose ? (
    <Button
      icon={<Lock />}
      disabled={!scopeCompanyId}
      title={!scopeCompanyId ? t('accounting.yearEndClosing.needCompany') : undefined}
      onClick={openClosing}
    >
      {t('accounting.yearEndClosing.action')}
    </Button>
  ) : null;

  const sectionOf = (line: DreLine) => dre?.sections.find((s) => s.line === line);

  const renderSection = (line: DreLine) => {
    const section = sectionOf(line);
    if (!section) return null;
    return (
      <Fragment key={line}>
        <tr className='dre-section'>
          <td style={{ fontWeight: 600 }}>{t(`accounting.dreLines.${line}`)}</td>
          <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
            {brlCents(section.total)}
          </td>
        </tr>
        {section.lines.map((l) => (
          <tr key={l.ledgerAccountId}>
            <td style={{ paddingLeft: 24 }}>
              <span className='t-mono'>{l.accountCode}</span> {l.accountName}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right' }}>
              {brlCents(l.amount)}
            </td>
          </tr>
        ))}
      </Fragment>
    );
  };

  const subtotal = (key: string, value: number) => (
    <tr className='dre-subtotal'>
      <td style={{ fontWeight: 600 }}>{t(`accounting.income.subtotals.${key}`)}</td>
      <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
        {brlCents(value)}
      </td>
    </tr>
  );

  const hasFinancial = !!sectionOf('FinancialRevenue') || !!sectionOf('FinancialExpense');

  return (
    <>
      <ReportHead title={t('accounting.income.title')} sub={t('accounting.income.subtitle')}>
        <ReportToolbar
          from={from}
          to={to}
          costCenterId={costCenterId}
          unitId={unitId}
          onFromChange={setFrom}
          onToChange={setTo}
          onCostCenterChange={setCostCenterId}
          onUnitChange={setUnitId}
          onExport={() => {
            if (invalidRange) return;
            void downloadWithAuth(
              api.incomeStatementExportUrl({
                companyId: scopeCompanyId,
                from: from || undefined,
                to: to || undefined,
                costCenterId: costCenterId || undefined,
                unitId: unitId || undefined,
              }),
              'dre.csv',
            );
          }}
        />
      </ReportHead>

      {invalidRange ? (
        <div className='help'>{t('accounting.trialBalance.invalidRange')}</div>
      ) : !scopeReady || query.isLoading ? (
        <LoadingState />
      ) : !hasData ? (
        <EmptyState
          icon={<LineChart />}
          title={t('accounting.income.empty.title')}
          sub={t('accounting.income.empty.sub')}
        />
      ) : (
        <Panel title={t('accounting.income.title')} action={closeAction}>
          {/* Cascata heterogênea (cabeçalho de seção, linha de conta, subtotal, resultado final coloridos
           *  de formas diferentes) sem <thead>: não é uma lista de entidades homogêneas, não cabe no
           *  modelo de papel por coluna do ResponsiveTable. .table-scroll evita que a coluna de valor
           *  fique inalcançável a 390px. */}
          <div className='table-scroll'>
            <table className='dre-table'>
              <tbody>
                {renderSection('GrossRevenue')}
                {renderSection('RevenueDeductions')}
                {subtotal('netRevenue', dre.netRevenue)}
                {renderSection('CostOfSales')}
                {subtotal('grossProfit', dre.grossProfit)}
                {renderSection('OtherOperatingIncome')}
                {renderSection('OperatingExpenses')}
                {subtotal('operatingResult', dre.operatingResult)}
                {renderSection('FinancialRevenue')}
                {renderSection('FinancialExpense')}
                {hasFinancial && subtotal('financialResult', dre.financialResult)}
                {renderSection('Other')}
                {subtotal('preTaxResult', dre.preTaxResult)}
                {renderSection('IncomeTax')}
                {renderSection('SocialContribution')}
                <tr className='dre-result'>
                  <td style={{ fontWeight: 700 }}>{t('accounting.income.subtotals.netIncome')}</td>
                  <td
                    className='amount-cell'
                    style={{
                      textAlign: 'right',
                      fontWeight: 700,
                      color: dre.netIncome >= 0 ? 'var(--success)' : 'var(--danger)',
                    }}
                  >
                    {brlCents(dre.netIncome)}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </Panel>
      )}

      {closingOpen && (
        <Modal
          title={t('accounting.yearEndClosing.title')}
          sub={t('accounting.yearEndClosing.sub')}
          onClose={() => setClosingOpen(false)}
          footer={
            <>
              <Button onClick={() => setClosingOpen(false)}>{t('forms.actions.cancel')}</Button>
              <Button
                variant='brand'
                disabled={closing.isPending || !closingYear}
                onClick={() => closing.mutate({ companyId: scopeCompanyId ?? '', fiscalYear: Number(closingYear) })}
              >
                {closing.isPending ? t('forms.actions.saving') : t('accounting.yearEndClosing.confirm')}
              </Button>
            </>
          }
        >
          <Field label={t('accounting.yearEndClosing.yearLabel')}>
            <Input type='number' value={closingYear} onChange={(e) => setClosingYear(e.target.value)} />
          </Field>
          <p className='help' style={{ marginTop: 10 }}>
            {t('accounting.yearEndClosing.warning')}
          </p>
        </Modal>
      )}
    </>
  );
}
