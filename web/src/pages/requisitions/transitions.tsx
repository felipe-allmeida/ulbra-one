import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Ban, Check, CornerUpLeft, Pencil, RotateCcw, Scale, SendHorizonal, Trash2, X } from 'lucide-react';
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
import { RequisitionDecisionPanel } from './decision-panel';
import { useToast } from '../../toast';
import { getErrorMessage } from '../../apiError';
import { useBudgetIssues } from './budget-guard';
import { triageApprovedToastKey } from './timeline';

/** Permissões × status → quais transições aparecem. Puro, para a página poder perguntar "há ações?" antes
 *  de montar o aside: um aside vazio abriria uma coluna de 360px sem conteúdo nenhum. */
export function transitionsFor(
  s: Requisition['status'],
  perms: { canWrite: boolean; canTriage: boolean; canApprove: boolean },
) {
  const { canWrite, canTriage, canApprove } = perms;
  const isTerminal = s === 'Paid' || s === 'Finalized' || s === 'Cancelled' || s === 'AwaitingFiscalDocument';
  const flags = {
    showSubmit: canWrite && s === 'Draft',
    showApproveTriage: canTriage && s === 'Triage',
    showApprove: canApprove && s === 'Approval',
    showReturn: (canTriage || canApprove) && (s === 'Triage' || s === 'Approval'),
    showResubmit: canWrite && s === 'Complement',
    showEdit: canWrite && (s === 'Draft' || s === 'Complement' || s === 'FinancialComplement'),
    showDelete: canWrite && s === 'Draft',
    showCancel: (canWrite || canTriage || canApprove) && !isTerminal,
  };
  return { ...flags, any: Object.values(flags).some(Boolean) };
}

/** `transitionsFor` com as permissões do usuário atual. Aceita `null` para poder ser chamado antes dos
 *  returns antecipados de carregamento/404 da página, sem quebrar a ordem dos hooks. */
export function useHasTransitionActions(req: Requisition | null): boolean {
  const { can } = usePermissions();
  if (!req) return false;
  return transitionsFor(req.status, {
    canWrite: can(P.requisitionsWrite),
    canTriage: can(P.requisitionsTriage),
    canApprove: can(P.requisitionsApprove),
  }).any;
}

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
  // Encerrar a solicitação de OUTRA pessoa, no estágio em que se decide sobre ela, é recusar — não cancelar.
  // Mesma transição (`Cancel` → status Cancelada), rótulo e diálogo diferentes: a recusa exige motivo, e o
  // histórico segue registrando "Cancelada" (o domínio não tem status `Rejected`).
  const isRejection = (canTriage && s === 'Triage') || (canApprove && s === 'Approval');

  const {
    showSubmit,
    showApproveTriage,
    showApprove,
    showReturn,
    showResubmit,
    showEdit,
    showDelete,
    showCancel,
    any,
  } = transitionsFor(s, { canWrite, canTriage, canApprove });

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

  // Sem nenhuma ação disponível o painel não existe — senão sobraria um cabeçalho "Sua decisão" vazio.
  if (!any) return null;

  return (
    <RequisitionDecisionPanel req={req}>
      {showSubmit && (
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

      {showApproveTriage && (
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

      {showApprove && (
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

      {showResubmit && (
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

      {showReturn && (
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

      {showEdit && (
        <Button icon={<Pencil />} onClick={() => nav(`/app/requisitions/${req.id}/edit`, { state: location.state })}>
          {t('requisitions.actions.edit')}
        </Button>
      )}

      {showCancel && (
        <Button
          variant='danger-outline'
          icon={isRejection ? <Ban /> : <X />}
          onClick={() => {
            setReason('');
            setCancelOpen(true);
          }}
        >
          {t(isRejection ? 'requisitions.decisionScreen.reject' : 'requisitions.actions.cancel')}
        </Button>
      )}

      {/* Excluir fica por último: é a única ação irreversível. */}
      {showDelete && (
        <Button variant='danger' icon={<Trash2 />} onClick={() => setDeleteOpen(true)}>
          {t('requisitions.actions.delete')}
        </Button>
      )}

      {returnOpen && (
        <ConfirmDialog
          title={t('requisitions.return.title')}
          confirmLabel={t('requisitions.return.confirm')}
          busy={returnForComplement.isPending}
          // O motivo É o pedido: sem ele a solicitação volta ao autor sem dizer o que ajustar.
          confirmDisabled={!reason.trim()}
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
          title={t(isRejection ? 'requisitions.decisionScreen.rejectTitle' : 'requisitions.cancel.title')}
          danger
          confirmLabel={t(isRejection ? 'requisitions.decisionScreen.rejectConfirm' : 'requisitions.cancel.confirm')}
          busy={cancel.isPending}
          // Recusar a solicitação de outra pessoa exige justificativa; desistir da própria, não.
          confirmDisabled={isRejection && !reason.trim()}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>
                {t(isRejection ? 'requisitions.decisionScreen.rejectMessage' : 'requisitions.cancel.message')}
              </div>
              <Textarea
                autoFocus
                value={reason}
                placeholder={t(
                  isRejection ? 'requisitions.decisionScreen.rejectPlaceholder' : 'requisitions.cancel.placeholder',
                )}
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
                  toast.success(
                    t(isRejection ? 'requisitions.decisionScreen.rejected' : 'requisitions.actions.cancelled'),
                  );
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
    </RequisitionDecisionPanel>
  );
}
