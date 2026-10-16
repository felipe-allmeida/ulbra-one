import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Check, Pencil, RotateCcw, SendHorizonal, Trash2, X } from 'lucide-react';
import { type BudgetAdjustment } from '../../api';
import {
  useApproveBudgetAdjustment,
  useCancelBudgetAdjustment,
  useDeleteBudgetAdjustment,
  useReverseBudgetAdjustment,
  useSendBudgetAdjustmentToApproval,
} from '../../queries';
import { P, usePermissions } from '../../permissions';
import { Button, ConfirmDialog, Input } from '../../ui';
import { useToast } from '../../toast';
import { getErrorMessage } from '../../apiError';

/**
 * Adjustment-workflow transition buttons, gated by (permission × current status).
 * The approver's limit (alçada) is NOT exposed on the front → we can't pre-disable "Approve";
 * the 409 `BudgetAdjustment.ApprovalLimitExceeded` is surfaced as a friendly toast.
 */
export function TransitionActions({ a }: { a: BudgetAdjustment }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const location = useLocation();
  const toast = useToast();
  const { can, me } = usePermissions();
  const canWrite = can(P.budgetAdjustmentsWrite);
  const canTriage = can(P.budgetAdjustmentsTriage);
  // Holding the role permission vs being individually enabled to approve (per-user grant, AEL-883): the
  // Approve/Reverse buttons require BOTH. Admin's me.canApproveBudgetAdjustment is always true.
  const hasApprovePermission = can(P.budgetAdjustmentsApprove);
  const canApprove = hasApprovePermission && (me?.canApproveBudgetAdjustment ?? false);

  const send = useSendBudgetAdjustmentToApproval();
  const approve = useApproveBudgetAdjustment();
  const cancel = useCancelBudgetAdjustment();
  const reverse = useReverseBudgetAdjustment();
  const del = useDeleteBudgetAdjustment({ onSuccess: () => nav('/app/budget-adjustments') });

  const [cancelOpen, setCancelOpen] = useState(false);
  const [reverseOpen, setReverseOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [reason, setReason] = useState('');

  const onError = (e: unknown) => toast.error(getErrorMessage(e));
  const s = a.status;
  const canCancel =
    (canWrite || canTriage || hasApprovePermission) && (s === 'AwaitingTriage' || s === 'AwaitingApproval');

  return (
    <div className='detail-actions detail-actions--end'>
      {canTriage && s === 'AwaitingTriage' && (
        <Button
          variant='brand'
          icon={<SendHorizonal />}
          disabled={send.isPending}
          onClick={() =>
            send.mutate(
              { id: a.id },
              { onError, onSuccess: () => toast.success(t('budgetAdjustments.actions.sentToApproval')) },
            )
          }
        >
          {t('budgetAdjustments.actions.sendToApproval')}
        </Button>
      )}

      {canApprove && s === 'AwaitingApproval' && (
        <Button
          className='btn-approve'
          icon={<Check />}
          disabled={approve.isPending}
          onClick={() =>
            approve.mutate(
              { id: a.id },
              { onError, onSuccess: () => toast.success(t('budgetAdjustments.actions.approved')) },
            )
          }
        >
          {t('budgetAdjustments.actions.approve')}
        </Button>
      )}

      {canApprove && s === 'Approved' && (
        <Button
          icon={<RotateCcw />}
          onClick={() => {
            setReason('');
            setReverseOpen(true);
          }}
        >
          {t('budgetAdjustments.actions.reverse')}
        </Button>
      )}

      {canWrite && s === 'AwaitingTriage' && (
        <Button
          icon={<Pencil />}
          onClick={() => nav(`/app/budget-adjustments/${a.id}/edit`, { state: location.state })}
        >
          {t('budgetAdjustments.actions.edit')}
        </Button>
      )}

      {canCancel && (
        <Button
          variant='danger-outline'
          icon={<X />}
          onClick={() => {
            setReason('');
            setCancelOpen(true);
          }}
        >
          {t('budgetAdjustments.actions.cancel')}
        </Button>
      )}

      {/* Delete is always last in the bar (most destructive, rightmost). */}
      {canWrite && s === 'AwaitingTriage' && (
        <Button variant='danger' icon={<Trash2 />} onClick={() => setDeleteOpen(true)}>
          {t('budgetAdjustments.actions.delete')}
        </Button>
      )}

      {cancelOpen && (
        <ConfirmDialog
          title={t('budgetAdjustments.cancel.title')}
          danger
          confirmLabel={t('budgetAdjustments.cancel.confirm')}
          busy={cancel.isPending}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('budgetAdjustments.cancel.message')}</div>
              <Input
                autoFocus
                value={reason}
                placeholder={t('budgetAdjustments.cancel.placeholder')}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            cancel.mutate(
              { id: a.id, reason: reason.trim() || undefined },
              {
                onError,
                onSuccess: () => {
                  setCancelOpen(false);
                  toast.success(t('budgetAdjustments.actions.cancelled'));
                },
              },
            )
          }
          onCancel={() => setCancelOpen(false)}
        />
      )}

      {reverseOpen && (
        <ConfirmDialog
          title={t('budgetAdjustments.reverse.title')}
          confirmLabel={t('budgetAdjustments.reverse.confirm')}
          busy={reverse.isPending}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('budgetAdjustments.reverse.message')}</div>
              <Input
                autoFocus
                value={reason}
                placeholder={t('budgetAdjustments.reverse.placeholder')}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            reverse.mutate(
              { id: a.id, reason: reason.trim() || undefined },
              {
                onError,
                onSuccess: () => {
                  setReverseOpen(false);
                  toast.success(t('budgetAdjustments.actions.reversed'));
                },
              },
            )
          }
          onCancel={() => setReverseOpen(false)}
        />
      )}

      {deleteOpen && (
        <ConfirmDialog
          title={t('budgetAdjustments.delete.title')}
          danger
          confirmLabel={t('budgetAdjustments.actions.delete')}
          busy={del.isPending}
          message={t('budgetAdjustments.delete.message')}
          onConfirm={() => del.mutate(a.id, { onError })}
          onCancel={() => setDeleteOpen(false)}
        />
      )}
    </div>
  );
}
