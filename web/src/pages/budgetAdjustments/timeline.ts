import i18n from '../../i18n';
import { type BudgetAdjustment, type BudgetAdjustmentTransition } from '../../api';
import { type AuditEvent } from '../../components/workflow';

/** Normalizes a user id for comparison (actorId arrives as `user_{guid:N}`). */
function normalizeUserId(id: string): string {
  return id
    .replace(/^user_/, '')
    .replace(/-/g, '')
    .toLowerCase();
}

/** Short id to display when it isn't the current user (there's no name lookup on the front). */
function shortId(id: string): string {
  const bare = id.replace(/^[a-z]+_/, '').replace(/-/g, '');
  return bare.length > 8 ? bare.slice(0, 8) : bare;
}

/** Maps the adjustment's transition history to `AuditTimeline` events. */
export function transitionsToEvents(transitions: BudgetAdjustmentTransition[], meId?: string | null): AuditEvent[] {
  const meNorm = meId ? normalizeUserId(meId) : null;
  return transitions.map((tr) => ({
    type: tr.action,
    actorName:
      meNorm && normalizeUserId(tr.actorId) === meNorm ? i18n.t('budgetAdjustments.timeline.you') : shortId(tr.actorId),
    timestamp: tr.occurredAt,
    metadata: { fromStatus: tr.fromStatus, toStatus: tr.toStatus, reason: tr.reason },
  }));
}

/** Most recent Cancel/Reverse reason (shown as a highlighted callout at the top of the detail). */
export function latestReason(a: BudgetAdjustment): { action: 'Cancel' | 'Reverse'; reason: string } | null {
  for (let i = a.transitions.length - 1; i >= 0; i--) {
    const tr = a.transitions[i];
    if ((tr.action === 'Cancel' || tr.action === 'Reverse') && tr.reason) {
      return { action: tr.action, reason: tr.reason };
    }
  }
  return null;
}
