import { type Requisition } from '../../api';
import { useProjectMetrics, useRequisitionBudgetControl } from '../../queries';

/**
 * Budget-overflow detection (AEL-830) — authoritative on the server. The control panel
 * (`/requisitions/{id}/budget-control`) returns the competencies the requisition breaches; the project
 * over-ceiling comes from `useProjectMetrics`. The hard block lives in `approve` — this hook only pre-disables
 * the Approve button (there is no override; a breach is resolved via a Budget Adjustment).
 */
export interface BudgetIssues {
  hasIssues: boolean;
  loading: boolean;
}

export function useBudgetIssues(req: Requisition | null, enabled: boolean): BudgetIssues {
  const active = enabled && !!req;
  const control = useRequisitionBudgetControl(req?.id, { enabled: active });
  const metrics = useProjectMetrics(active ? (req?.projectId ?? undefined) : undefined).data;

  if (!req || !enabled) return { hasIssues: false, loading: false };

  // Conta Fixa (AEL-881, R1) pode ser enviada/aprovada ACIMA do teto — o estouro continua VISÍVEL no painel/gráfico
  // (o valor entra no orçamento consumido), mas nunca bloqueia a aprovação. Este é o espelho de UX da regra do
  // domínio `Requisition.BypassesBudgetCeiling` (fonte da verdade; o servidor `approve` aplica a mesma isenção).
  if (req.type === 'FixedExpense') return { hasIssues: false, loading: control.isLoading };

  const breaches = control.data?.breaches ?? [];
  const projectOverCeiling = metrics?.isOverCeiling ?? false;

  return {
    hasIssues: breaches.length > 0 || projectOverCeiling,
    loading: control.isLoading,
  };
}

/** Row badge state: a real breach ("Estoura orçamento") vs a merely unbudgeted competency ("Sem orçamento"). */
export type BudgetBadge = 'none' | 'unbudgeted' | 'over';

/**
 * Pure classifier for the budget row indicator (AEL-928). The server folds unbudgeted months INTO `breaches`
 * (AEL-796), so a missing budget cell used to render the same red "estoura orçamento" as a real overage. Here
 * we split them back out via the separate `unbudgeted` list, mirroring `decision-budget.tsx`. Conta Fixa
 * bypasses the ceiling (the server now returns no breaches for it — `BudgetControlService`), so it never flags.
 * Shared by `useBudgetBadge` (per-row hook) and the batched asides that already hold the control data.
 */
export function budgetBadgeFor(
  data: { breaches: readonly { competency: string }[]; unbudgeted: readonly string[] } | undefined,
  type: Requisition['type'],
): BudgetBadge {
  if (!data || type === 'FixedExpense') return 'none';
  const unbudgeted = new Set(data.unbudgeted);
  if (data.breaches.some((b) => !unbudgeted.has(b.competency))) return 'over';
  if (data.unbudgeted.length > 0) return 'unbudgeted';
  return 'none';
}

/** Per-row budget indicator for the Triage/Approval inboxes. See {@link budgetBadgeFor}. */
export function useBudgetBadge(req: Requisition | null): { badge: BudgetBadge; loading: boolean } {
  const control = useRequisitionBudgetControl(req?.id, { enabled: !!req });
  return {
    badge: req ? budgetBadgeFor(control.data, req.type) : 'none',
    loading: req ? control.isLoading : false,
  };
}
