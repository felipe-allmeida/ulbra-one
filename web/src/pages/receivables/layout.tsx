import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Plus, Wallet } from 'lucide-react';
import { useApp } from '../../store';
import { useReceivableStageSummary } from '../../queries';
import { PageHeader, Button, EmptyState } from '../../ui';
import { EmpresaPicker } from '../shared';
import { PendencyDashboard, StageTabBar, type StageTab } from '../../components/workflow';
import { TAB_ORDER, dashboardCards, cardTargetTab, isTabKey, type TabKey } from './stages';
import { AReceberTab } from './areceber-tab';
import { ReconciledTab } from './reconciled-tab';

export function ReceivablesLayout() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { empresa, empresaId } = useApp();
  const [params, setParams] = useSearchParams();

  const tabParam = params.get('stage');
  const active: TabKey = isTabKey(tabParam) ? tabParam : 'areceber';
  const setStage = (key: string) =>
    setParams((prev) => {
      prev.set('stage', key);
      return prev;
    });

  const summaryQ = useReceivableStageSummary(empresaId);
  const summary = summaryQ.data;

  const cards = summary ? dashboardCards(summary, t) : [];
  const tabs: StageTab[] = TAB_ORDER.map((key) => {
    let count: number | undefined;
    if (summary) {
      count = key === 'areceber' ? summary.entry.count + summary.receipt.count : summary.reconciled.count;
    }
    return {
      key,
      label: t(`receivables.tabs.${key}`),
      count,
    };
  });

  return (
    <>
      <PageHeader title={t('receivables.title')} sub={t('receivables.subtitle')}>
        <EmpresaPicker />
        <Button
          variant='brand'
          icon={<Plus />}
          disabled={!empresa}
          style={{ alignSelf: 'flex-end' }}
          onClick={() => {
            void nav('/app/receivables/novo');
          }}
        >
          {t('receivables.new')}
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Wallet />}
          title={t('receivables.empty.noCompanyTitle')}
          sub={t('receivables.empty.noCompanySub')}
        />
      ) : (
        <>
          {cards.length > 0 && (
            <PendencyDashboard cards={cards} onCardClick={(cardKey) => setStage(cardTargetTab(cardKey))} />
          )}
          <StageTabBar tabs={tabs} active={active} onChange={setStage} />
          {active === 'areceber' && <AReceberTab companyId={empresaId} />}
          {active === 'reconciled' && <ReconciledTab companyId={empresaId} />}
        </>
      )}
    </>
  );
}
