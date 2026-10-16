import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ClipboardList, Plus } from 'lucide-react';
import { type RequisitionStatus } from '../../api';
import { useApp } from '../../store';
import { useRequisitionStageCounts } from '../../queries';
import { P, usePermissions } from '../../permissions';
import { Button, EmptyState, PageHeader } from '../../ui';
import { StageTabBar, type StageTab } from '../../components/workflow';
import { STAGE_ORDER, STAGE_STATUS, isStageKey, type StageKey } from './stages';
import { RequisitionStageTab } from './list-tab';

type ActiveStage = Exclude<StageKey, 'encerradas'>;
const COUNT_STATUSES: RequisitionStatus[] = (STAGE_ORDER.filter((s) => s !== 'encerradas') as ActiveStage[]).map(
  (s) => STAGE_STATUS[s],
);

export function RequisitionsLayout() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { empresa, empresaId, isAllCompanies, scopeReady } = useApp();
  const { can } = usePermissions();
  const [params, setParams] = useSearchParams();

  const stageParam = params.get('stage');
  const active: StageKey = isStageKey(stageParam) ? stageParam : 'rascunhos';
  const setStage = (key: string) =>
    setParams((prev) => {
      prev.set('stage', key);
      prev.delete('page'); // page is stage-scoped: a fresh stage starts on page 1
      return prev;
    });

  // The Requester dashboard navigates with ?mine=1 (own only); ?closed=<status> pre-selects the status
  // chip on the "Encerradas" tab (terminal cards like "Pagas" land there) — read inside RequisitionStageTab.
  const mine = params.get('mine') === '1';
  const { counts } = useRequisitionStageCounts(empresaId, COUNT_STATUSES, { mine });

  const tabs: StageTab[] = STAGE_ORDER.map((key) => ({
    key,
    label: t(`requisitions.stages.${key}`),
    count: key === 'encerradas' ? undefined : counts[STAGE_STATUS[key]],
    badge:
      (key === 'triagem' && (counts.Triage ?? 0) > 0) || (key === 'aprovacao' && (counts.Approval ?? 0) > 0)
        ? 'warning'
        : undefined,
  }));

  return (
    <>
      <PageHeader title={t('requisitions.title')} sub={t('requisitions.subtitle')}>
        {/* Consolidated "Todas" (AEL-906) is read-only: creating needs a specific company. */}
        {can(P.requisitionsWrite) && !isAllCompanies && (
          <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => nav('/app/requisitions/new')}>
            {t('requisitions.new')}
          </Button>
        )}
      </PageHeader>

      {!scopeReady ? (
        <EmptyState
          icon={<ClipboardList />}
          title={t('requisitions.empty.noCompany')}
          sub={t('requisitions.empty.noCompanySub')}
        />
      ) : (
        <>
          <StageTabBar tabs={tabs} active={active} onChange={setStage} />
          <RequisitionStageTab companyId={empresaId} stage={active} mine={mine} />
        </>
      )}
    </>
  );
}
