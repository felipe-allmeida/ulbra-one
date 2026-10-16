import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Check, CornerUpLeft, Pencil, RotateCcw, Scale, SendHorizonal, Trash2, X } from 'lucide-react';
import { type Requisition } from '../../api';
import {
  useApproveRequisition,
  useApproveTriage,
  useCancelRequisition,
  useDeleteRequisition,
  useResubmitRequisition,
  useReturnRequisitionForComplement,
  useSubmitRequisition,
} from '../../queries';
import { P, usePermissions } from '../../permissions';
import { Button, ConfirmDialog, Textarea } from '../../ui';
import { useToast } from '../../toast';
import { getErrorMessage } from '../../apiError';
import { useBudgetIssues } from './budget-guard';
import { triageApprovedToastKey } from './timeline';

/**
 * Workflow transition buttons, conditioned on (permission × current status).
 * The Approver has no alçada: the only block on "Aprovar" is the budget ceiling, pre-disabled
 * when the estimate overflows (the 409 `Requisition.BudgetExceeded` guides to the Adequação).
 */
export function TransitionActions({ req }: { req: Requisition }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const location = useLocation();
  const toast = useToast();
  const { can } = usePermissions();
  const canWrite = can(P.requisitionsWrite);
  const canTriage = can(P.requisitionsTriage);
  const canApprove = can(P.requisitionsApprove);

  const submit = useSubmitRequisition();
  const approveTriage = useApproveTriage();
  const approve = useApproveRequisition();
  const resubmit = useResubmitRequisition();
  const returnForComplement = useReturnRequisitionForComplement();
  const cancel = useCancelRequisition();
  const del = useDeleteRequisition({ onSuccess: () => nav('/app/requisitions') });

  const [returnOpen, setReturnOpen] = useState(false);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [reason, setReason] = useState('');

  const onError = (e: unknown) => toast.error(getErrorMessage(e));
  const s = req.status;
  const isTerminal = s === 'Paid' || s === 'Finalized' || s === 'Cancelled' || s === 'AwaitingFiscalDocument';
  const canCancel = (canWrite || canTriage || canApprove) && !isTerminal;

  // Overage guard (AEL-830): the block is authoritative on the server; here we anticipate the modal.
  const issues = useBudgetIssues(req, canApprove && s === 'Approval');
  const doApprove = () =>
    approve.mutate(
      { id: req.id },
      {
        onError,
        onSuccess: () => toast.success(t('requisitions.actions.approved')),
      },
    );

  return (
    <div className='detail-actions detail-actions--end'>
      {canWrite && s === 'Draft' && (
        <Button
          variant='brand'
          icon={<SendHorizonal />}
          disabled={submit.isPending}
          onClick={() =>
            submit.mutate(
              { id: req.id },
              {
                onError,
                onSuccess: () => toast.success(t('requisitions.actions.submittedToTriage')),
              },
            )
          }
        >
          {t('requisitions.actions.submit')}
        </Button>
      )}

      {canTriage && s === 'Triage' && (
        <Button
          variant='brand'
          icon={<Check />}
          disabled={approveTriage.isPending}
          onClick={() =>
            approveTriage.mutate(
              { id: req.id },
              { onError, onSuccess: (updated) => toast.success(t(triageApprovedToastKey(updated.status))) },
            )
          }
        >
          {t('requisitions.actions.approveTriage')}
        </Button>
      )}

      {/* The financial complement submission (with installments + issuance) is done by the form in the detail
          (FinancialComplementForm), not by a header button — AEL-844. */}

      {canApprove && s === 'Approval' && (
        <>
          <Button
            className='btn-approve'
            icon={<Check />}
            // Approver has no override: an overage is a hard block → the button is disabled (resolve via a Budget Adjustment).
            disabled={approve.isPending || issues.loading || issues.hasIssues}
            onClick={() => doApprove()}
          >
            {t('requisitions.actions.approve')}
          </Button>
          {/* Only whoever can CREATE the Adequação sees the shortcut — for the final approver the route is gated and the
              button would become a dead-end (silent redirect to home). */}
          {issues.hasIssues && can(P.budgetAdjustmentsWrite) && (
            <Button icon={<Scale />} onClick={() => nav('/app/budget-adjustments/new')}>
              {t('requisitions.actions.openBudgetAdjustment')}
            </Button>
          )}
        </>
      )}

      {canWrite && s === 'Complement' && (
        <Button
          variant='brand'
          icon={<RotateCcw />}
          disabled={resubmit.isPending}
          onClick={() =>
            resubmit.mutate(
              { id: req.id },
              { onError, onSuccess: () => toast.success(t('requisitions.actions.resubmitted')) },
            )
          }
        >
          {t('requisitions.actions.resubmit')}
        </Button>
      )}

      {(canTriage || canApprove) && (s === 'Triage' || s === 'Approval') && (
        <Button
          icon={<CornerUpLeft />}
          onClick={() => {
            setReason('');
            setReturnOpen(true);
          }}
        >
          {t('requisitions.actions.return')}
        </Button>
      )}

      {canWrite && (s === 'Draft' || s === 'Complement' || s === 'FinancialComplement') && (
        <Button icon={<Pencil />} onClick={() => nav(`/app/requisitions/${req.id}/edit`, { state: location.state })}>
          {t('requisitions.actions.edit')}
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
          {t('requisitions.actions.cancel')}
        </Button>
      )}

      {/* Delete is always last in the bar (most destructive, rightmost). */}
      {canWrite && s === 'Draft' && (
        <Button variant='danger' icon={<Trash2 />} onClick={() => setDeleteOpen(true)}>
          {t('requisitions.actions.delete')}
        </Button>
      )}

      {returnOpen && (
        <ConfirmDialog
          title={t('requisitions.return.title')}
          confirmLabel={t('requisitions.return.confirm')}
          busy={returnForComplement.isPending}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('requisitions.return.message')}</div>
              <Textarea
                autoFocus
                value={reason}
                placeholder={t('requisitions.return.placeholder')}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            returnForComplement.mutate(
              { id: req.id, reason: reason.trim() || undefined },
              {
                onError,
                onSuccess: () => {
                  setReturnOpen(false);
                  toast.success(t('requisitions.actions.returned'));
                },
              },
            )
          }
          onCancel={() => setReturnOpen(false)}
        />
      )}

      {cancelOpen && (
        <ConfirmDialog
          title={t('requisitions.cancel.title')}
          danger
          confirmLabel={t('requisitions.cancel.confirm')}
          busy={cancel.isPending}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('requisitions.cancel.message')}</div>
              <Textarea
                autoFocus
                value={reason}
                placeholder={t('requisitions.cancel.placeholder')}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            cancel.mutate(
              { id: req.id, reason: reason.trim() || undefined },
              {
                onError,
                onSuccess: () => {
                  setCancelOpen(false);
                  toast.success(t('requisitions.actions.cancelled'));
                },
              },
            )
          }
          onCancel={() => setCancelOpen(false)}
        />
      )}

      {deleteOpen && (
        <ConfirmDialog
          title={t('requisitions.delete.title')}
          danger
          confirmLabel={t('requisitions.actions.delete')}
          busy={del.isPending}
          message={t('requisitions.delete.message')}
          onConfirm={() => del.mutate(req.id, { onError })}
          onCancel={() => setDeleteOpen(false)}
        />
      )}
    </div>
  );
}
