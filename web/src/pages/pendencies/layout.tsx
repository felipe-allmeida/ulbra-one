import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { ClipboardCheck } from 'lucide-react';
import { useApp } from '../../store';
import { usePayableStageSummary } from '../../queries';
import { PageHeader, EmptyState } from '../../ui';
import { EmpresaPicker } from '../shared';
import { StageTabBar, type StageTab } from '../../components/workflow';
import { ApprovalTab } from '../payables';
import { TAB_ORDER, isTabKey, type PendencyTabKey } from './tabs';
import { FiscalTab } from './fiscal-tab';

/**
 * "Pendências" — action-required items grouped by category (replaces the old Aprovações screen).
 * Operates entirely on the Payables workflow: Aprovação = PendingApproval, Nota Fiscal = PendingAccounting
 * without a registered NF. The tab set is extensible via TAB_ORDER.
 */
export function PendenciesPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const [params, setParams] = useSearchParams();

  // These are pendency categories, not workflow stages, so the URL param is `tab` (not `stage`).
  const tabParam = params.get('tab');
  const active: PendencyTabKey = isTabKey(tabParam) ? tabParam : 'approval';
  const setTab = (key: string) =>
    setParams((prev) => {
      prev.set('tab', key);
      return prev;
    });

  const summary = usePayableStageSummary(empresaId).data;
  const counts: Record<PendencyTabKey, number | undefined> = {
    approval: summary?.approval.count,
    fiscal: summary?.fiscalDocumentPending.count,
  };
  const tabs: StageTab[] = TAB_ORDER.map((key) => ({
    key,
    label: t(`pendencies.tabs.${key}`),
    count: counts[key],
    badge: (counts[key] ?? 0) > 0 ? 'warning' : undefined,
  }));

  return (
    <>
      <PageHeader title={t('pendencies.title')} sub={t('pendencies.subtitle')}>
        <EmpresaPicker />
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<ClipboardCheck />}
          title={t('payables.empty.noCompanyTitle')}
          sub={t('payables.empty.noCompanySub')}
        />
      ) : (
        <>
          <StageTabBar tabs={tabs} active={active} onChange={setTab} />
          {active === 'approval' && <ApprovalTab companyId={empresaId} />}
          {active === 'fiscal' && <FiscalTab companyId={empresaId} />}
        </>
      )}
    </>
  );
}
