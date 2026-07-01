import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { Percent } from 'lucide-react';
import { useWithholdingsReport } from '../queries';
import { brlCents } from '../lib';
import { EmptyState, PageHeader, Panel } from '../ui';
import { ConsolidatedPicker } from './contabil-shared';

export function ContabilRetencoesPage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  const query = useWithholdingsReport(companyId || undefined);
  const report = query.data;
  const hasData = (report?.byPayable.length ?? 0) > 0;

  return (
    <>
      <PageHeader title={t('accounting.withholdings.title')} sub={t('accounting.withholdings.subtitle')}>
        <ConsolidatedPicker value={companyId} onChange={setCompanyId} />
      </PageHeader>

      {query.isLoading ? (
        <div className='help'>{t('accounting.journal.loading')}</div>
      ) : !hasData ? (
        <EmptyState
          icon={<Percent />}
          title={t('accounting.withholdings.empty.title')}
          sub={t('accounting.withholdings.empty.sub')}
        />
      ) : (
        <>
          <div className='info-grid' style={{ marginBottom: 16 }}>
            <div className='info-card'>
              <div className='info-label'>{t('accounting.withholdings.gross')}</div>
              <div className='info-value'>{brlCents(report?.totals.grossTotal ?? 0)}</div>
            </div>
            <div className='info-card'>
              <div className='info-label'>{t('accounting.withholdings.withheld')}</div>
              <div className='info-value'>{brlCents(report?.totals.withheldTotal ?? 0)}</div>
            </div>
            <div className='info-card'>
              <div className='info-label'>{t('accounting.withholdings.net')}</div>
              <div className='info-value'>{brlCents(report?.totals.netTotal ?? 0)}</div>
            </div>
          </div>

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
                {(report?.byTax ?? []).map((r) => (
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
                    {brlCents(report?.totals.withheldTotal ?? 0)}
                  </td>
                  <td />
                </tr>
              </tfoot>
            </table>
          </Panel>

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
                {(report?.byPayable ?? []).map((p) => (
                  <tr key={p.id}>
                    <td>
                      <Link to={`/app/payables/${p.id}`}>{p.counterpartyName}</Link>
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
        </>
      )}
    </>
  );
}
