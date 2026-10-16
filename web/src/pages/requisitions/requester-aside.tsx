import { useTranslation } from 'react-i18next';
import { Inbox, Wallet } from 'lucide-react';
import { type CostCenterBudgetLine } from '../../api';
import { useCostCenterBudgetSummary, useRequisitionStageCounts } from '../../queries';
import { brlCents } from '../../lib';
import { cx, LoadingState } from '../../ui';
import { Bar, money, Tile } from './budget-tiles';
import { currentCompetency } from './competency';

/**
 * Requester aside (AEL-836 / artifact "Home por papel"). Two layouts share this component:
 * - `home`: the full unit budget list + a queue-tiles ("Sua fila") panel.
 * - `form`: a focused panel for the unit + cost center selected in the requisition form on top (whose
 *   "Solicitado"/"Disponível" and bar update live with the value being typed), then the full unit budget
 *   list below it. No queue tiles here (irrelevant while filling a new request).
 * Counts are scoped to the user's own requisitions (mine).
 */
export function RequesterAside({
  empresaId,
  variant = 'home',
  selectedCostCenterId,
  selectedUnitId,
  requestValue = 0,
}: {
  empresaId: string;
  variant?: 'home' | 'form';
  /** Cost center (natureza) picked in the form — drives the focused panel + list highlight. */
  selectedCostCenterId?: string;
  /** Unit picked in the form — scopes the budget summary to that unit (AEL-891); undefined = company-wide. */
  selectedUnitId?: string;
  /** Estimated value being typed in the form — previewed live against the selected natureza's budget. */
  requestValue?: number;
}) {
  const { t } = useTranslation();
  const competency = currentCompetency();
  const budget = useCostCenterBudgetSummary(empresaId, competency, selectedUnitId);
  const {
    counts,
    totals,
    isLoading: countsLoading,
  } = useRequisitionStageCounts(
    empresaId,
    ['Complement', 'FinancialComplement', 'Draft', 'Triage', 'Approval', 'InPayment', 'Paid', 'Finalized'],
    { mine: true },
  );

  const needsYou = (counts.Complement ?? 0) + (counts.FinancialComplement ?? 0) + (counts.Draft ?? 0);
  const tracking = (counts.Triage ?? 0) + (counts.Approval ?? 0) + (counts.InPayment ?? 0);
  const done = (counts.Paid ?? 0) + (counts.Finalized ?? 0);
  // Summed estimated value in approval — comes from the same grouped query (server-side sum, so it covers
  // every requisition in approval, not just the first page the old per-page list fetch could see).
  const inApprovalTotal = totals.Approval ?? 0;
  const budgetLines = budget.data ?? [];
  const selected = selectedCostCenterId ? budgetLines.find((l) => l.costCenterId === selectedCostCenterId) : undefined;

  if (budget.isLoading || countsLoading) return <LoadingState />;

  return (
    <>
      {variant === 'form' && (
        <div className='spanel'>
          <div className='sph'>
            <div className='spt'>
              <Wallet aria-hidden />
              {t('requisitions.requesterHome.selectedBudgetTitle')}
            </div>
            <div className='sps'>{competency}</div>
          </div>
          <div className='spbody'>
            {selected ? (
              <SelectedBudget line={selected} requestValue={requestValue} />
            ) : (
              <div className='bud-row-note'>{t('requisitions.requesterHome.selectNatureza')}</div>
            )}
          </div>
        </div>
      )}

      {/* "Sua fila" (counters) before "Orçamentos da unidade" — the Requester prioritizes their own queue. */}
      {variant === 'home' && (
        <div className='spanel'>
          <div className='sph'>
            <div className='spt'>
              <Inbox aria-hidden />
              {t('requisitions.requesterHome.aside.title')}
            </div>
          </div>
          <div className='spbody'>
            <div className='kpitiles'>
              <Tile
                n={String(needsYou)}
                l={t('requisitions.requesterHome.aside.needsYou')}
                tone={needsYou > 0 ? 'warn' : undefined}
              />
              <Tile n={String(tracking)} l={t('requisitions.requesterHome.aside.tracking')} />
              <Tile n={String(done)} l={t('requisitions.requesterHome.aside.done')} />
              <Tile {...money(inApprovalTotal)} l={t('requisitions.requesterHome.aside.inApproval')} />
            </div>
          </div>
        </div>
      )}

      {budgetLines.length > 0 && (
        <div className='spanel'>
          <div className='sph'>
            <div className='spt'>
              <Wallet aria-hidden />
              {t('requisitions.requesterHome.budgetTitle')}
            </div>
            <div className='sps'>{competency}</div>
          </div>
          <div className='spbody'>
            {budgetLines.map((l) => (
              <BudgetRow key={l.costCenterId} line={l} highlight={l.costCenterId === selectedCostCenterId} />
            ))}
          </div>
        </div>
      )}
    </>
  );
}

/** Focused panel body: the selected natureza's REAL numbers + a live preview of the value being typed.
 * Disponível is the REAL one (only InPayment+ consumes); "Após aprovação" projects the available if this request is
 * approved (available − requested). The request being typed does not reserve the available until it goes to payment. */
function SelectedBudget({ line, requestValue }: { line: CostCenterBudgetLine; requestValue: number }) {
  const { t } = useTranslation();
  const available = line.available; // REAL available
  const afterApproval = available - requestValue; // projection "if approved"
  const over = requestValue > 0 && requestValue > available;
  return (
    <>
      <div className='kpitiles'>
        <Tile {...money(line.orcado)} l={t('requisitions.requesterHome.orcado')} />
        <Tile {...money(available)} l={t('requisitions.requesterHome.disponivel')} />
        <Tile {...money(requestValue)} l={t('requisitions.requesterHome.solicitado')} />
        <Tile
          {...money(afterApproval)}
          l={t('requisitions.requesterHome.afterApproval')}
          tone={over ? 'bad' : undefined}
        />
      </div>
      <Bar orcado={line.orcado} used={line.consumed} queued={requestValue} over={over} />
    </>
  );
}

/** One "orçamento por natureza" row: name + available, a consumed/queued bar, and a numeric sub-line.
 * Also used in the Approver dashboard aside (AEL-859). */
export function BudgetRow({ line, highlight }: { line: CostCenterBudgetLine; highlight?: boolean }) {
  const { t } = useTranslation();
  const over = line.available < 0;
  return (
    <div className={cx('bud-row', highlight && 'bud-row--on')}>
      <div className='bud-row-head'>
        <span className='bud-row-name'>{line.costCenterName}</span>
        <span className={cx('bud-row-avail', over && 'bad')}>{brlCents(line.available)}</span>
      </div>
      {line.orcado > 0 ? (
        <>
          <Bar orcado={line.orcado} used={line.consumed} queued={line.forecast} over={over} />
          <div className='bud-row-sub'>
            {t('requisitions.requesterHome.budgetRowSub', {
              orcado: brlCents(line.orcado),
              consumed: brlCents(line.consumed),
            })}
          </div>
        </>
      ) : (
        <div className='bud-row-note'>{t('requisitions.requesterHome.noBudget')}</div>
      )}
    </div>
  );
}
