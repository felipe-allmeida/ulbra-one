import {
  AlarmClock,
  Bell,
  CircleCheck,
  CircleX,
  ClipboardCheck,
  FileText,
  Inbox,
  Paperclip,
  PencilLine,
  RotateCcw,
  Undo2,
  type LucideIcon,
} from 'lucide-react';

/**
 * Presentation for each notification `type` the backend emits (see `NotificationMapper` in
 * `Ulbra.One.Notifications.Infrastructure`). The type is a stable wire key — the frontend already
 * switches on it to build the deep link — so it is also what decides the icon, the colour and whether
 * the row belongs to a team.
 *
 * `team` mirrors `Notification.ForRole` vs `ForUser` on the backend, which the API does not expose as a
 * field. It matters because read state is shared on role-addressed rows: one person marking it read
 * clears it for the whole team, and the "Equipe" chip is what makes that visible instead of surprising.
 */
export type NotificationTone = 'info' | 'success' | 'warning' | 'danger';

export interface NotificationMeta {
  icon: LucideIcon;
  tone: NotificationTone;
  team: boolean;
}

/** Tones read as outcomes: info = waiting on someone, success = it went through, warning = the reader
 *  has to do something, danger = it died or blew its SLA. */
const CATALOGUE: Record<string, NotificationMeta> = {
  RequisitionAwaitingTriage: { icon: Inbox, tone: 'info', team: true },
  RequisitionAwaitingApproval: { icon: ClipboardCheck, tone: 'info', team: true },
  RequisitionAwaitingFinancialComplement: { icon: PencilLine, tone: 'warning', team: false },
  RequisitionReturnedForComplement: { icon: Undo2, tone: 'warning', team: false },
  RequisitionApproved: { icon: CircleCheck, tone: 'success', team: false },
  RequisitionCancelled: { icon: CircleX, tone: 'danger', team: false },
  RequisitionFiscalDocumentRequested: { icon: FileText, tone: 'warning', team: false },
  RequisitionFiscalDocumentAttached: { icon: Paperclip, tone: 'success', team: false },
  RequisitionTriageSlaOverdue: { icon: AlarmClock, tone: 'danger', team: true },
  RequisitionApprovalSlaOverdue: { icon: AlarmClock, tone: 'danger', team: true },
  BudgetAdjustmentAwaitingTriage: { icon: Inbox, tone: 'info', team: true },
  BudgetAdjustmentAwaitingApproval: { icon: ClipboardCheck, tone: 'info', team: true },
  BudgetAdjustmentApproved: { icon: CircleCheck, tone: 'success', team: false },
  BudgetAdjustmentCancelled: { icon: CircleX, tone: 'danger', team: false },
  BudgetAdjustmentReversed: { icon: RotateCcw, tone: 'warning', team: false },
};

/** A type the frontend has not caught up with yet still renders — as a neutral bell, never blank. */
const FALLBACK: NotificationMeta = { icon: Bell, tone: 'info', team: false };

export const notificationMeta = (type: string): NotificationMeta => CATALOGUE[type] ?? FALLBACK;
