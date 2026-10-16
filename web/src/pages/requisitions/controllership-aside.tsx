import { useTranslation } from 'react-i18next';
import { Inbox, Wallet } from 'lucide-react';
import { useCostCenterBudgetSummary, useQueueBudgetControls, useRequisitions } from '../../queries';
import { brl } from '../../lib';
import { LoadingState } from '../../ui';
import { budgetBadgeFor } from './budget-guard';
import { Tile } from './budget-tiles';
import { BudgetRow } from './requester-aside';
import { QueueProjectionPanel, RequisitionProjectionPanel } from './budget-projection-panel';
import { currentCompetency } from './competency';

/**
 * Aside of the Controllership screens (Entrega 4): TRIAGE queue (within the role's permissions) + budget
 * by nature. Mirrors ApproverAside, swapping the queue status (Triage) and the title; reused by the
 * triage Inbox and, by role, by the Dashboard/Budgets. `showQueueProjection` (inbox) and `projectionReqId`
 * (decision screen) turn on the "Impacto no orçamento" sparkline — never both at the same time.
 */
export function ControllershipAside({
  empresaId,
  unitId,
  showQueueProjection,
  projectionReqId,
}: {
  empresaId: string;
  unitId?: string;
  showQueueProjection?: boolean;
  projectionReqId?: string;
}) {
  const { t } = useTranslation();
  const competency = currentCompetency();
  const queue = useRequisitions(
    { companyId: empresaId, status: 'Triage', mine: false, unitId, page: 1, pageSize: 50 },
    { enabled: !!empresaId },
  );
  const items = queue.data?.items ?? [];
  const controls = useQueueBudgetControls(items.map((r) => r.id));
  const evaluated = controls.filter((c) => c.data !== undefined).length;
  // Only a real breach counts as "precisa de adequação" — unbudgeted and Conta Fixa are not overages (AEL-928).
  const overCount = controls.filter((c, i) => items[i] && budgetBadgeFor(c.data, items[i].type) === 'over').length;
  const queueValue = items.reduce((s, r) => s + r.estimateValue, 0);
  const budget = useCostCenterBudgetSummary(empresaId, competency, unitId);
  const budgetLines = budget.data ?? [];

  if (queue.isLoading || budget.isLoading) return <LoadingState />;

  return (
    <>
      {projectionReqId ? (
        <RequisitionProjectionPanel reqId={projectionReqId} />
      ) : showQueueProjection ? (
        <QueueProjectionPanel companyId={empresaId} unitId={unitId} />
      ) : null}

      <div className='spanel'>
        <div className='sph'>
          <div className='spt'>
            <Inbox aria-hidden />
            {t('requisitions.triageInbox.asideQueueTitle')}
          </div>
        </div>
        <div className='spbody'>
          <div className='kpitiles'>
            <Tile n={String(queue.data?.total ?? 0)} l={t('requisitions.overview.asidePending')} />
            <Tile n={brl(queueValue)} l={t('requisitions.overview.asideQueueValue')} />
            <Tile
              n={t('requisitions.overview.asideFitValue', { fit: evaluated - overCount, total: evaluated })}
              l={t('requisitions.overview.asideFit')}
            />
            <Tile
              n={String(overCount)}
              l={t('requisitions.overview.asideNeedAdjustment')}
              tone={overCount > 0 ? 'bad' : undefined}
            />
          </div>
        </div>
      </div>

      {budgetLines.length > 0 && (
        <div className='spanel'>
          <div className='sph'>
            <div className='spt'>
              <Wallet aria-hidden />
              {t('requisitions.overview.asideBudgetTitle')}
            </div>
            <div className='sps'>{competency}</div>
          </div>
          <div className='spbody'>
            {budgetLines.map((l) => (
              <BudgetRow key={l.costCenterId} line={l} />
            ))}
          </div>
        </div>
      )}
    </>
  );
}
