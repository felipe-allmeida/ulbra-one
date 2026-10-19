import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Scale } from 'lucide-react';
import { useApp } from '../../store';
import { useTrialBalance } from '../../queries';
import { startOfCurrentMonth, endOfCurrentMonth } from '../../datetime';
import { EmptyState, LoadingState } from '../../ui';
import { ReportHead, ReportToolbar } from '../contabil-shared';
import { BalanceteTable } from './table';
import { downloadTrialBalanceExport } from './download';

/** Balancete de verificação — read-only, per account: opening balance, period debit/credit and closing
 *  balance for an optional competence range, optionally filtered by cost center. The empresa/consolidado
 *  scope comes from the topbar selector (`useApp`), not from a page filter.
 *  Same header/filters as the DRE (shared `ReportToolbar`, single row). */
export function BalancetePage() {
  const { t } = useTranslation();
  const { scopeCompanyId, scopeReady } = useApp();
  const [from, setFrom] = useState(startOfCurrentMonth);
  const [to, setTo] = useState(endOfCurrentMonth);
  const [costCenterId, setCostCenterId] = useState('');
  const [unitId, setUnitId] = useState('');

  // Guard the invalid competence range client-side: the endpoint returns 400 InvalidRange for from > to, which
  // would otherwise surface as the generic "no movement" empty state. Skip the query and show a clear hint.
  const invalidRange = !!(from && to && from > to);
  const query = useTrialBalance(
    {
      companyId: scopeCompanyId,
      from: from || undefined,
      to: to || undefined,
      costCenterId: costCenterId || undefined,
      unitId: unitId || undefined,
    },
    { enabled: !invalidRange && scopeReady },
  );
  const report = query.data;
  const rows = report?.rows ?? [];

  return (
    <>
      <ReportHead title={t('accounting.trialBalance.title')} sub={t('accounting.trialBalance.subtitle')}>
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
            void downloadTrialBalanceExport({
              companyId: scopeCompanyId,
              from: from || undefined,
              to: to || undefined,
              costCenterId: costCenterId || undefined,
              unitId: unitId || undefined,
            });
          }}
        />
      </ReportHead>

      {invalidRange ? (
        <div className='help'>{t('accounting.trialBalance.invalidRange')}</div>
      ) : !scopeReady || query.isLoading ? (
        <LoadingState />
      ) : rows.length === 0 || !report ? (
        <EmptyState
          icon={<Scale />}
          title={t('accounting.trialBalance.empty.title')}
          sub={t('accounting.trialBalance.empty.sub')}
        />
      ) : (
        <BalanceteTable report={report} from={from} to={to} costCenterId={costCenterId} unitId={unitId} />
      )}
    </>
  );
}
