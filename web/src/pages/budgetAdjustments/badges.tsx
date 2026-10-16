import { useTranslation } from 'react-i18next';
import { type BudgetAdjustment, type BudgetAdjustmentStatus, type BudgetAdjustmentType } from '../../api';
import { Chip } from '../../ui';
import { stageSla } from '../requisitions/sla';

/** Status colors: awaiting = amber, approved = green, reversed/cancelled = red. */
const STATUS_CLS: Record<BudgetAdjustmentStatus, string> = {
  AwaitingTriage: 'badge-pendente',
  AwaitingApproval: 'badge-pendente',
  Approved: 'badge-pago',
  Reversed: 'badge-rejeitado',
  Cancelled: 'badge-rejeitado',
};

export function BudgetAdjustmentStatusBadge({ s }: { s: BudgetAdjustmentStatus }) {
  const { t } = useTranslation();
  return <span className={`badge-status ${STATUS_CLS[s]}`}>{t(`budgetAdjustments.status.${s}`)}</span>;
}

export function BudgetAdjustmentTypeChip({ type }: { type: BudgetAdjustmentType }) {
  const { t } = useTranslation();
  return <Chip>{t(`budgetAdjustments.type.${type}`)}</Chip>;
}

/** SLA badge (24 business hours) — only in the stages with a deadline (Triage/Approval). Reuses the AEL-790 util. */
export function BudgetAdjustmentSlaChip({ a, now }: { a: BudgetAdjustment; now?: Date }) {
  const { t } = useTranslation();
  if (a.status !== 'AwaitingTriage' && a.status !== 'AwaitingApproval') return null;
  const sla = stageSla(a.statusChangedAt, 24, now);
  if (!sla) return null;
  const text = sla.overdue
    ? t('budgetAdjustments.sla.overdue', { hours: -sla.remaining })
    : t('budgetAdjustments.sla.remaining', { hours: sla.remaining });
  return <span className={`sla-chip sla-${sla.tone}`}>{text}</span>;
}
