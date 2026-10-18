import { useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Ban, Check, CornerUpLeft, Pencil, Wallet } from 'lucide-react';
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
import { Button, Input, Panel, EmptyState, Drawer, ConfirmDialog, Field, LoadingState } from '../../ui';
import { useMediaQuery } from '../../useMediaQuery';
import { brl } from '../../lib';
import { fmtDate } from '../../datetime';
import { AsideLayout, AuditTimeline, DecisionPanel, DecisionRow } from '../../components/workflow';
import { BackLink } from '../../nav';
import { PayableSummaryCard } from './summary-card';
import { ContabilAside } from './contabil-aside';
import { TributacaoAside } from './tributacao-aside';
import { TreasuryAside } from '../treasury/treasury-aside';
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
  const [changesOpen, setChangesOpen] = useState(false);
  const [returnTarget, setReturnTarget] = useState<PayableReturnTarget>('Requester');
  const [returnReason, setReturnReason] = useState('');
  const [attToDelete, setAttToDelete] = useState<PayableAttachment | null>(null);
  const [baixaInstallment, setBaixaInstallment] = useState<Installment | null>(null);
  // Acima de 1200px o aside é a coluna sticky de 360px; abaixo disso ele desempilha para o fim da página.
  const asideIsSticky = useMediaQuery('(min-width: 1200px)');

  if (query.isLoading) return <LoadingState />;
  if (!p)
    return (
      <EmptyState icon={<Wallet />} title={t('payables.empty.notFoundTitle')} sub={t('payables.empty.notFoundSub')} />
    );

  const canEdit = p.status === 'PendingApproval' || p.status === 'Received' || p.status === 'PendingClassification';
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

  // Encerrar é sempre terminal para quem usa. O endpoint muda com a permissão, não com a intenção:
  // `cancel` exige payables:write, `reject` exige payables:approve — colapsar num só tiraria a saída de
  // quem tem apenas uma das duas. Ambos produzem "Rejeitada" na tela.
  const rejectViaCancel = canWrite && (canEdit || p.status === 'Scheduled');
  const rejectViaApproval = canApprove && p.status === 'PendingApproval';
  const showReject = rejectViaCancel || rejectViaApproval;
  const showApprove = canApprove && p.status === 'PendingApproval';

  const roleAside = can(P.payablesTax) ? (
    <TributacaoAside companyId={p.companyId} />
  ) : can(P.payablesPay) || canApprove ? (
    <TreasuryAside companyId={p.companyId} />
  ) : canWrite ? (
    <ContabilAside companyId={p.companyId} />
  ) : undefined;

  const nextOpen = [...p.installments].sort((a, b) => a.sequence - b.sequence).find((i) => i.status !== 'Paid');
  const paidTotal = p.installments.reduce(
    (sum, i) => sum + i.payments.filter((x) => !x.isReversed).reduce((a, x) => a + x.amount, 0),
    0,
  );

  const decisionPanel = (showApprove || canReturn || showReject) && (
    <DecisionPanel
      title={t('payables.decisionPanel.title')}
      subtitle={`${p.documentNumber || t('payables.decisionPanel.noDocument')} · ${brl(p.totalAmount)}`}
      context={
        <>
          {nextOpen && <DecisionRow label={t('payables.decisionPanel.nextDue')} value={fmtDate(nextOpen.dueDate)} />}
          {paidTotal > 0 && <DecisionRow label={t('payables.decisionPanel.paid')} value={brl(paidTotal)} />}
          <DecisionRow label={t('payables.decisionPanel.open')} value={brl(p.totalAmount - paidTotal)} tone='warn' />
        </>
      }
    >
      {showApprove && (
        <Button className='btn-approve' icon={<Check />} onClick={() => approve.mutate({ id: p.id })}>
          {t('payables.actions.approve')}
        </Button>
      )}
      {/* Reversível: a requisição volta para correção e, na reaprovação, ESTE título é reaberto. O destino
          (solicitante ou controladoria) é uma escolha dentro do diálogo — eram dois botões com o mesmo ícone. */}
      {canReturn && (
        <Button
          icon={<CornerUpLeft />}
          onClick={() => {
            setReturnReason('');
            setReturnTarget('Requester');
            setChangesOpen(true);
          }}
        >
          {t('payables.actions.requestChanges')}
        </Button>
      )}
      {showReject && (
        <Button
          variant='danger-outline'
          icon={<Ban />}
          onClick={() => {
            setCancelReason('');
            setCancelOpen(true);
          }}
        >
          {t('payables.actions.reject')}
        </Button>
      )}
    </DecisionPanel>
  );

  return (
    <AsideLayout
      // Abaixo de 1200px o painel desce para a coluna única; sem aside de papel, não há coluna a abrir.
      aside={
        (asideIsSticky && decisionPanel) || roleAside ? (
          <>
            {asideIsSticky && decisionPanel}
            {roleAside}
          </>
        ) : undefined
      }
    >
      {/* Sem origem conhecida (deep-link/aba nova), o voltar leva à Gestão Financeira — home do Financeiro —,
          não a Contas a Pagar; quando há origem real (location.state.from) ela prevalece. */}
      <BackLink fallback='/app/treasury' />
      {!asideIsSticky && decisionPanel}

      <div className='detail-stack'>
        <PayableSummaryCard
          payable={p}
          action={
            canWrite && canEdit ? (
              <Button icon={<Pencil />} onClick={() => nav(`/app/payables/${p.id}/edit`, { state: location.state })}>
                {t('payables.actions.edit')}
              </Button>
            ) : undefined
          }
        />

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
          title={t('payables.reject.title')}
          danger
          busy={cancel.isPending || reject.isPending}
          confirmLabel={t('payables.reject.confirm')}
          // O motivo entra na trilha e é o que a contraparte vê; sem ele a rejeição não se explica.
          confirmDisabled={!cancelReason.trim()}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payables.reject.message')}</div>
              <Input
                autoFocus
                value={cancelReason}
                placeholder={t('payables.reject.placeholder')}
                onChange={(e) => setCancelReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() => {
            const reason = cancelReason.trim();
            const done = { onSuccess: () => setCancelOpen(false) };
            // `cancel` (payables:write) é o caminho normal; `reject` (payables:approve) cobre quem só aprova.
            if (rejectViaCancel) cancel.mutate({ id: p.id, reason }, done);
            else reject.mutate({ id: p.id, reason }, done);
          }}
          onCancel={() => setCancelOpen(false)}
        />
      )}

      {changesOpen && (
        <ConfirmDialog
          title={t('payables.requestChanges.title')}
          busy={returnPayable.isPending}
          confirmDisabled={!returnReason.trim()} // o motivo é o pedido: é o que o solicitante lê na requisição
          confirmLabel={t('payables.requestChanges.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 12 }}>{t('payables.requestChanges.message')}</div>
              {/* O destino era dois botões com o mesmo ícone na barra; aqui é uma escolha, que é o que ele é. */}
              <Field label={t('payables.requestChanges.target')}>
                <select
                  className='input select'
                  value={returnTarget}
                  onChange={(e) => setReturnTarget(e.target.value as PayableReturnTarget)}
                >
                  <option value='Requester'>{t('payables.requestChanges.targetRequester')}</option>
                  <option value='Controllership'>{t('payables.requestChanges.targetControllership')}</option>
                </select>
              </Field>
              <div style={{ marginTop: 12 }}>
                <Field label={t('payables.requestChanges.reason')}>
                  <Input
                    autoFocus
                    value={returnReason}
                    placeholder={t('payables.requestChanges.placeholder')}
                    onChange={(e) => setReturnReason(e.target.value)}
                  />
                </Field>
              </div>
            </div>
          }
          onConfirm={() =>
            returnPayable.mutate(
              { id: p.id, reason: returnReason.trim(), returnTo: returnTarget },
              { onSuccess: () => setChangesOpen(false) },
            )
          }
          onCancel={() => setChangesOpen(false)}
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
    </AsideLayout>
  );
}
