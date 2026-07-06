import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Percent } from 'lucide-react';
import { useWithholdingsReport } from '../../queries';
import type { WithholdingPayableRow } from '../../api';
import { brlCents } from '../../lib';
import { EmptyState, PageHeader } from '../../ui';
import { ConsolidatedPicker } from '../contabil-shared';
import { ByTaxTable } from './by-tax-table';
import { ByPayableTable } from './by-payable-table';
import { WithholdingDetailDrawer } from './detail-drawer';

export function RetencoesPage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  // The row whose per-tax breakdown is shown in the side drawer (lines already come in the report GET).
  const [selected, setSelected] = useState<WithholdingPayableRow | null>(null);
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

          <ByTaxTable rows={report?.byTax ?? []} withheldTotal={report?.totals.withheldTotal ?? 0} />
          <ByPayableTable rows={report?.byPayable ?? []} onSelect={setSelected} />
        </>
      )}

      <WithholdingDetailDrawer row={selected} onClose={() => setSelected(null)} />
    </>
  );
}
