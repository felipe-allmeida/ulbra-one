import i18n from '../../i18n';
import { type Requisition, type RequisitionTransition } from '../../api';
import { type AuditEvent } from '../../components/workflow';

/**
 * Normalizes a user id for comparison: `createdBy` arrives as a raw Guid
 * (with hyphens) and `transition.actorId` / `me.userId` as `user_{guid:N}`.
 */
function normalizeUserId(id: string): string {
  return id
    .replace(/^user_/, '')
    .replace(/-/g, '')
    .toLowerCase();
}

/** short id to display when neither the backend nor the action tells who the actor was. */
function shortId(id: string): string {
  const bare = id.replace(/^[a-z]+_/, '').replace(/-/g, '');
  return bare.length > 8 ? bare.slice(0, 8) : bare;
}

/** Role inferred from the transition (the action determines who could have executed it; ReturnForComplement/Cancel
 * disambiguate by the origin status). Only a fallback now: the detail endpoint resolves the actor's real name,
 * and the lists (which do not) still read better as a role than as a Guid. */
function actorRole(tr: RequisitionTransition): string | null {
  const roles = (k: string) => i18n.t(`requisitions.timeline.roles.${k}`);
  switch (tr.action) {
    case 'Submit':
    case 'Resubmit':
    case 'SubmitFinancialComplement':
    case 'ReturnToTriageForRevalue':
      return roles('requester');
    case 'ApproveTriage':
      return roles('controllership');
    case 'Approve':
      return roles('approver');
    case 'ReturnFromPayment':
      return roles('finance');
    case 'ReturnForComplement':
      return tr.fromStatus === 'Triage' ? roles('controllership') : roles('approver');
    case 'Cancel':
      if (tr.fromStatus === 'Triage') return roles('controllership');
      if (tr.fromStatus === 'Approval') return roles('approver');
      return null;
    default:
      return null;
  }
}

/** Toast i18n key for a triage-approval result: the route (front-loaded plan → Approval, else
 * FinancialComplement) is only known from the resulting status, so the message follows it. */
export function triageApprovedToastKey(status: string): string {
  return status === 'Approval'
    ? 'requisitions.actions.triageApprovedToApproval'
    : 'requisitions.actions.triageApprovedToComplement';
}

/** Maps the transition history to the `AuditTimeline` events. */
export function transitionsToEvents(transitions: RequisitionTransition[], meId?: string | null): AuditEvent[] {
  const meNorm = meId ? normalizeUserId(meId) : null;
  return transitions.map((tr) => ({
    // ApproveTriage routes two ways (front-loaded plan → Approval, else → FinancialComplement); the label must
    // say which, so a front-loaded approval reads "liberada direto para aprovação" instead of the complemento note.
    type: tr.action === 'ApproveTriage' && tr.toStatus === 'Approval' ? 'ApproveTriageToApproval' : tr.action,
    actorName:
      meNorm && normalizeUserId(tr.actorId) === meNorm
        ? i18n.t('requisitions.timeline.you')
        : (tr.actorName ?? actorRole(tr) ?? shortId(tr.actorId)),
    actorPictureUrl: tr.actorPictureUrl,
    timestamp: tr.occurredAt,
    metadata: { fromStatus: tr.fromStatus, toStatus: tr.toStatus, reason: tr.reason },
  }));
}

/** Return/cancellation reason as a top banner, but ONLY while it is the current operative state — i.e. the
 * last transition is a return-to-requester (Complement), a return-from-payment, or a cancellation. Once
 * resolved (Resubmit/ApproveTriage appended after), it drops off the banner and lives on only in the
 * timeline, so the approver no longer sees a devolução the requester already addressed. Mirrors
 * retriageReason (last-transition-only). */
export type ReasonAction = 'ReturnForComplement' | 'ReturnToFinancialComplement' | 'ReturnFromPayment' | 'Cancel';

const REASON_ACTIONS: readonly ReasonAction[] = [
  'ReturnForComplement',
  // Sem isto o motivo escrito em "Solicitar ajuste nas parcelas" só existiria no Histórico — o solicitante
  // abre o Complemento financeiro sem saber o que ajustar (o FinancialComplementForm não exibe motivo).
  'ReturnToFinancialComplement',
  'ReturnFromPayment',
  'Cancel',
];

export function activeReason(req: Requisition): { action: ReasonAction; reason: string } | null {
  const last = req.transitions[req.transitions.length - 1];
  if (last && REASON_ACTIONS.includes(last.action as ReasonAction) && last.reason) {
    return { action: last.action as ReasonAction, reason: last.reason };
  }
  return null;
}

/** If the requisition bounced back to triage for revaluation (value raised above the triage-approved amount,
 * or the vendor changed in the financial complement), returns the transition for the triage screen's callout. */
export function retriageReason(req: Requisition): RequisitionTransition | null {
  const last = req.transitions[req.transitions.length - 1];
  return last?.action === 'ReturnToTriageForRevalue' ? last : null;
}
