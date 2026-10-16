import { useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Check, X, Wallet, Pencil, CornerUpLeft } from 'lucide-react';
import { type Installment, type PayableAttachment, type PayableReturnTarget } from '../../api';
import {
  useApprovePayable,
  useCancelPayable,
  useDeletePayableAttachment,
  usePayable,
  usePayableAuditTrail,
  useRejectPayable,
  useReturnPayable,
} from '../../queries';
import { P, usePermissions } from '../../permissions';
import { Button, Input, Panel, EmptyState, Drawer, ConfirmDialog, LoadingState } from '../../ui';
import { AuditTimeline, BalanceForecastBanner } from '../../components/workflow';
import { BackLink } from '../../nav';
import { PayableSummaryCard } from './summary-card';
import { InstallmentsTable } from './installments-table';
import { AttachmentsPanel } from './attachments-panel';
import { PaymentHistory } from './payment-history';
import { PaymentForm } from './payment-form';
import { WithholdingSection } from './withholding-section';

/* ============ DETAIL ============ */
export function PayableDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const location = useLocation();
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canWrite = can(P.payablesWrite);
  const canApprove = can(P.payablesApprove);
  const query = usePayable(id);
  const p = query.data ?? null;
  const audit = usePayableAuditTrail(id);

  const approve = useApprovePayable();
  const reject = useRejectPayable();
  const returnPayable = useReturnPayable();
  const cancel = useCancelPayable();
  const deleteAttachment = useDeletePayableAttachment();

  const [rejectOpen, setRejectOpen] = useState(false);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [cancelReason, setCancelReason] = useState('');
  // Return-to-role dialog for a requisition-sourced payable (AEL-935); null = closed.
  const [returnTarget, setReturnTarget] = useState<PayableReturnTarget | null>(null);
  const [returnReason, setReturnReason] = useState('');
  const [attToDelete, setAttToDelete] = useState<PayableAttachment | null>(null);
  const [baixaInstallment, setBaixaInstallment] = useState<Installment | null>(null);

  if (query.isLoading) return <LoadingState />;
  if (!p)
    return (
      <EmptyState icon={<Wallet />} title={t('payables.empty.notFoundTitle')} sub={t('payables.empty.notFoundSub')} />
    );

  const canEdit = p.status === 'PendingApproval' || p.status === 'Received' || p.status === 'PendingClassification';
  // A scheduled payable can't be edited, but it CAN be cancelled (the server allows it while unpaid) — this is the
  // operator remediation for an orphaned requisition payable: reverse the payments, then cancel here.
  const canCancel = canWrite && (canEdit || p.status === 'Scheduled');
  // Return to the requester/controllership (AEL-935): a Finance action (payables:write) available on any
  // requisition-sourced title that hasn't been paid/cancelled/returned — requisition titles auto-schedule, so
  // this is normally used from the payment stage (Agendada).
  const canReturn =
    canWrite && !!p.originRequisitionId && p.status !== 'Paid' && p.status !== 'Cancelled' && p.status !== 'Rejected';
  // Withholdings belong to Tax (SetWithholdings is gated by payables:tax on the backend, AEL-948), not
  // Accounting/Finance. Editable in any pre-scheduling state; the domain locks it at Scheduled/Paid/Rejected/Cancelled.
  const withholdingsEditable =
    can(P.payablesTax) &&
    p.status !== 'Scheduled' &&
    p.status !== 'Paid' &&
    p.status !== 'Rejected' &&
    p.status !== 'Cancelled';

  return (
    <>
      {/* Sem origem conhecida (deep-link/aba nova), o voltar leva à Gestão Financeira — home do Financeiro —,
          não a Contas a Pagar; quando há origem real (location.state.from) ela prevalece. */}
      <BackLink fallback='/app/treasury' />
      <div className='detail-actions detail-actions--end'>
        {p.status === 'PendingApproval' && (
          <BalanceForecastBanner companyId={p.companyId} additionalOutflow={p.totalAmount} />
        )}
        {canApprove && p.status === 'PendingApproval' && (
          <Button className='btn-approve' icon={<Check />} onClick={() => approve.mutate({ id: p.id })}>
            {t('payables.actions.approve')}
          </Button>
        )}
        {/* A requisition-sourced payable is returned (below), never terminally rejected. */}
        {canApprove && p.status === 'PendingApproval' && !p.originRequisitionId && (
          <Button variant='danger-outline' icon={<X />} onClick={() => setRejectOpen(true)}>
            {t('payables.actions.reject')}
          </Button>
        )}
        {canWrite && canEdit && (
          <Button icon={<Pencil />} onClick={() => nav(`/app/payables/${p.id}/edit`, { state: location.state })}>
            {t('payables.actions.edit')}
          </Button>
        )}
        {/* Return a requisition-sourced payable to the requester or controllership (AEL-935). Cancelar stays
            the rightmost action, so these render before it. */}
        {canReturn && (
          <>
            <Button
              icon={<CornerUpLeft />}
              onClick={() => {
                setReturnReason('');
                setReturnTarget('Requester');
              }}
            >
              {t('payables.actions.returnToRequester')}
            </Button>
            <Button
              icon={<CornerUpLeft />}
              onClick={() => {
                setReturnReason('');
                setReturnTarget('Controllership');
              }}
            >
              {t('payables.actions.returnToControllership')}
            </Button>
          </>
        )}
        {canCancel && (
          <Button variant='danger-outline' icon={<X />} onClick={() => setCancelOpen(true)}>
            {t('payables.actions.cancel')}
          </Button>
        )}
      </div>

      <div className='detail-stack'>
        <PayableSummaryCard payable={p} />

        <div className='detail-sections'>
          <InstallmentsTable payable={p} onRegister={setBaixaInstallment} />

          <WithholdingSection payable={p} editable={withholdingsEditable} />

          {[...p.installments]
            .sort((a, b) => a.sequence - b.sequence)
            .filter((inst) => inst.payments.length > 0)
            .map((inst) => (
              <Panel
                key={inst.id}
                title={`${t('payments.title')} — ${t('installments.label')} ${inst.sequence}/${p.installments.length}`}
              >
                <PaymentHistory payable={p} installment={inst} />
              </Panel>
            ))}

          <AttachmentsPanel payable={p} onDelete={setAttToDelete} />

          <Panel title={t('payables.cards.timeline')}>
            <AuditTimeline events={audit.data ?? []} loading={audit.isLoading} />
          </Panel>
        </div>
      </div>

      {rejectOpen && (
        <ConfirmDialog
          title={t('payables.reject.title')}
          danger
          busy={reject.isPending}
          confirmLabel={t('payables.reject.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payables.reject.message')}</div>
              <Input
                autoFocus
                value={rejectReason}
                placeholder={t('payables.reject.placeholder')}
                onChange={(e) => setRejectReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            reject.mutate({ id: p.id, reason: rejectReason.trim() }, { onSuccess: () => setRejectOpen(false) })
          }
          onCancel={() => setRejectOpen(false)}
        />
      )}

      {cancelOpen && (
        <ConfirmDialog
          title={t('payables.cancel.title')}
          danger
          busy={cancel.isPending}
          confirmLabel={t('payables.cancel.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payables.cancel.message')}</div>
              <Input
                autoFocus
                value={cancelReason}
                placeholder={t('payables.cancel.placeholder')}
                onChange={(e) => setCancelReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            cancel.mutate({ id: p.id, reason: cancelReason.trim() }, { onSuccess: () => setCancelOpen(false) })
          }
          onCancel={() => setCancelOpen(false)}
        />
      )}

      {returnTarget && (
        <ConfirmDialog
          title={t('payables.return.title')}
          busy={returnPayable.isPending}
          confirmDisabled={!returnReason.trim()} // the reason is what the requester reads on the requisition
          confirmLabel={t('payables.return.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>
                {t(
                  returnTarget === 'Controllership'
                    ? 'payables.return.messageControllership'
                    : 'payables.return.messageRequester',
                )}
              </div>
              <Input
                autoFocus
                value={returnReason}
                placeholder={t('payables.return.placeholder')}
                onChange={(e) => setReturnReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            returnPayable.mutate(
              { id: p.id, reason: returnReason.trim(), returnTo: returnTarget },
              { onSuccess: () => setReturnTarget(null) },
            )
          }
          onCancel={() => setReturnTarget(null)}
        />
      )}

      <Drawer
        open={!!baixaInstallment}
        title={t('payments.registerTitle')}
        sub={
          baixaInstallment
            ? `${t('installments.label')} ${baixaInstallment.sequence}/${p.installments.length}`
            : undefined
        }
        onClose={() => setBaixaInstallment(null)}
      >
        {baixaInstallment && (
          <PaymentForm
            payable={p}
            installment={baixaInstallment}
            onDone={() => setBaixaInstallment(null)}
            onCancel={() => setBaixaInstallment(null)}
          />
        )}
      </Drawer>

      {attToDelete && (
        <ConfirmDialog
          title={t('attachments.deleteTitle')}
          danger
          busy={deleteAttachment.isPending}
          confirmLabel={t('attachments.remove')}
          message={t('attachments.deleteMessage', { name: attToDelete.fileName })}
          onConfirm={() =>
            deleteAttachment.mutate(
              { id: p.id, attachmentId: attToDelete.id },
              { onSuccess: () => setAttToDelete(null) },
            )
          }
          onCancel={() => setAttToDelete(null)}
        />
      )}
    </>
  );
}
