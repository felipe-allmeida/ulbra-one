import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Percent } from 'lucide-react';
import { useApp } from '../../store';
import { useWithholdingsReport } from '../../queries';
import type { WithholdingPayableRow } from '../../api';
import { brlCents } from '../../lib';
import { EmptyState, PageHeader, LoadingState } from '../../ui';
import { ByTaxTable } from './by-tax-table';
import { ByPayableTable } from './by-payable-table';
import { WithholdingDetailDrawer } from './detail-drawer';

/** Relatório de retenções. The empresa/consolidado scope comes from the topbar selector (`useApp`),
 *  not from a page filter. */
export function RetencoesPage() {
  const { t } = useTranslation();
  const { scopeCompanyId, scopeReady } = useApp();
  // The row whose per-tax breakdown is shown in the side drawer (lines already come in the report GET).
  const [selected, setSelected] = useState<WithholdingPayableRow | null>(null);
  const query = useWithholdingsReport(scopeCompanyId, { enabled: scopeReady });
  const report = query.data;
  const hasData = (report?.byPayable.length ?? 0) > 0;

  return (
    <>
      <PageHeader title={t('accounting.withholdings.title')} sub={t('accounting.withholdings.subtitle')} />

      {!scopeReady || query.isLoading ? (
        <LoadingState />
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
              <div className='info-label'>{t('accounting.withholdings.realized')}</div>
              <div className='info-value'>{brlCents(report?.totals.realizedTotal ?? 0)}</div>
            </div>
            <div className='info-card'>
              <div className='info-label'>{t('accounting.withholdings.foreseen')}</div>
              <div className='info-value'>{brlCents(report?.totals.foreseenTotal ?? 0)}</div>
            </div>
            <div className='info-card'>
              <div className='info-label'>{t('accounting.withholdings.net')}</div>
              <div className='info-value'>{brlCents(report?.totals.netTotal ?? 0)}</div>
            </div>
          </div>

          {(report?.totals.foreseenTotal ?? 0) > 0 && (
            <div className='help' style={{ marginBottom: 12 }}>
              {t('accounting.withholdings.foreseenHint')}
            </div>
          )}

          <ByTaxTable
            rows={report?.byTax ?? []}
            realizedTotal={report?.totals.realizedTotal ?? 0}
            foreseenTotal={report?.totals.foreseenTotal ?? 0}
          />
          <ByPayableTable rows={report?.byPayable ?? []} onSelect={setSelected} />
        </>
      )}

      <WithholdingDetailDrawer row={selected} onClose={() => setSelected(null)} />
    </>
  );
}
