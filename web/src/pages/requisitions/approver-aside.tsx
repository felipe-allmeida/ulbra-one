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
 * Shared aside of the Approver screens (AEL-859): approval queue (within the role's
 * permissions — no cash data) + unit budget by nature. Reused in the
 * Dashboard, the Inbox and Budgets. `showQueueProjection` (inbox) and `projectionReqId` (decision
 * screen) turn on the "Impacto no orçamento" sparkline (AEL-876) that succeeds the mock's "Impacto no caixa".
 */
export function ApproverAside({
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
    { companyId: empresaId, status: 'Approval', mine: false, unitId, page: 1, pageSize: 50 },
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
            {t('requisitions.overview.asideQueueTitle')}
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
