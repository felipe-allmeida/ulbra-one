import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Plus, Wallet } from 'lucide-react';
import { useApp } from '../../store';
import { usePayableStageSummary } from '../../queries';
import { P, usePermissions } from '../../permissions';
import { PageHeader, Button, EmptyState } from '../../ui';
import { PendencyDashboard, StageTabBar, type StageTab } from '../../components/workflow';
import { STAGE_ORDER, dashboardCards, isStageKey, type StageKey } from './stages';
import { EntryTab } from './entry-tab';
import { ClassificationTab } from './classification-tab';
import { ApprovalTab } from './approval-tab';
import { AccountingTab } from './accounting-tab';
import { TaxAnalysisTab } from './tax-analysis-tab';
import { PaymentTab } from './payment-tab';
import { ReconciledTab } from './reconciled-tab';

export function PayablesLayout() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { empresa, empresaId, isAllCompanies, scopeReady } = useApp();
  const { can } = usePermissions();
  const [params, setParams] = useSearchParams();

  const stageParam = params.get('stage');
  const active: StageKey = isStageKey(stageParam) ? stageParam : 'entry';
  const setStage = (key: string) =>
    setParams((prev) => {
      prev.set('stage', key);
      prev.delete('page'); // page is tab-scoped: a fresh stage starts on page 1
      return prev;
    });

  const summaryQ = usePayableStageSummary(empresaId);
  const summary = summaryQ.data;

  const cards = summary ? dashboardCards(summary, t) : [];
  const tabs: StageTab[] = STAGE_ORDER.map((key) => ({
    key,
    label: t(`payables.stages.${key}`),
    count: summary ? summary[key].count : undefined,
    badge:
      (key === 'approval' && summary && summary.approval.count > 0) ||
      (key === 'accounting' && summary && summary.accounting.count > 0)
        ? 'warning'
        : undefined,
  }));

  return (
    <>
      <PageHeader title={t('payables.title')} sub={t('payables.subtitle')}>
        {/* Consolidated "Todas" (AEL-906) is read-only: creating needs a specific company. */}
        {can(P.payablesWrite) && !isAllCompanies && (
          <Button
            variant='brand'
            icon={<Plus />}
            disabled={!empresa}
            style={{ alignSelf: 'flex-end' }}
            onClick={() => {
              void nav('/app/payables/new');
            }}
          >
            {t('payables.new')}
          </Button>
        )}
      </PageHeader>

      {!scopeReady ? (
        <EmptyState
          icon={<Wallet />}
          title={t('payables.empty.noCompanyTitle')}
          sub={t('payables.empty.noCompanySub')}
        />
      ) : (
        <>
          {cards.length > 0 && <PendencyDashboard cards={cards} onCardClick={setStage} />}
          <StageTabBar tabs={tabs} active={active} onChange={setStage} />
          {active === 'entry' && <EntryTab companyId={empresaId} />}
          {active === 'classification' && <ClassificationTab companyId={empresaId} />}
          {active === 'approval' && <ApprovalTab companyId={empresaId} />}
          {active === 'accounting' && <AccountingTab companyId={empresaId} />}
          {active === 'taxAnalysis' && <TaxAnalysisTab companyId={empresaId} />}
          {active === 'payment' && <PaymentTab companyId={empresaId} />}
          {active === 'reconciled' && <ReconciledTab companyId={empresaId} />}
        </>
      )}
    </>
  );
}
