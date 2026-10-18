import { useState } from 'react';
import { Navigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ClipboardList, CornerUpLeft, X } from 'lucide-react';
import { type RequisitionAttachment } from '../../api';
import { useDeleteRequisitionAttachment, useMe, useProjectMetrics, useRequisition } from '../../queries';
import { P, usePermissions } from '../../permissions';
import { ConfirmDialog, EmptyState, Panel, LoadingState } from '../../ui';
import { AuditTimeline, AsideLayout } from '../../components/workflow';
import { BackLink } from '../../nav';
import { PresenceAvatars } from '../../realtime/PresenceAvatars';
import { useMediaQuery } from '../../useMediaQuery';
import { RequisitionPipeline } from './pipeline';
import { TransitionActions, useHasTransitionActions } from './transitions';
import { AttachmentsPanel } from './attachments-panel';
import { BudgetForecast } from './budget-forecast';
import { DecisionBudgetPanel } from './decision-budget';
import { FinancialComplementForm } from './financial-complement-form';
import { RecurrenceCyclesPanel } from './recurrence-cycles';
import { RequesterAside } from './requester-aside';
import { ControllershipAside } from './controllership-aside';
import { ApproverAside } from './approver-aside';
import { RequisitionSummaryCard } from './summary-card';
import { FinancialPlanView } from './financial-plan-view';
import { ProjectBudgetCard } from './project-budget-card';
import { competencyWindowText, monthsBetween, spansCompetencyWindow } from './competency';
import { activeReason, transitionsToEvents, type ReasonAction } from './timeline';

export function RequisitionDetailPage() {
  const { id } = useParams();
  const { t } = useTranslation();
  const query = useRequisition(id);
  const req = query.data ?? null;
  const me = useMe().data;
  const { can, finalApprover } = usePermissions();
  const projectMetrics = useProjectMetrics(req?.projectId ?? undefined).data;
  const deleteAttachment = useDeleteRequisitionAttachment();
  const [attToDelete, setAttToDelete] = useState<RequisitionAttachment | null>(null);
  // Acima de 1200px o aside vira a coluna sticky de 360px (ver `.aside-layout`); abaixo disso ele desempilha
  // para o fim da página, então as ações sobem para o topo da coluna única.
  const asideIsSticky = useMediaQuery('(min-width: 1200px)');
  const hasActions = useHasTransitionActions(req);

  // The final approver decides on the dedicated screen — covers deep-links and the bell link (AEL-859).
  if (finalApprover && id) return <Navigate to={`/app/requisitions/approval/${id}`} replace />;

  if (query.isLoading) return <LoadingState />;
  if (!req) return <EmptyState icon={<ClipboardList />} title={t('requisitions.empty.notFound')} />;

  const reason = activeReason(req);
  const competencyText = competencyWindowText(req, t('requisitions.competencyWindow.recurring'));
  // EstimateValue is the TOTAL; the forecast overrides the contribution PER competency. Approximates uniformly
  // (total ÷ months in the window) — the server does the exact split with remainder-on-last.
  const windowMonths = req.endCompetency ? monthsBetween(req.startCompetency, req.endCompetency) : 1;
  const perCompetencyValue =
    spansCompetencyWindow(req) && windowMonths > 1 ? req.estimateValue / windowMonths : req.estimateValue;

  // Requester authoring the financial complement: the installments form + the attachments + the submit button.
  const authoringComplement = req.status === 'FinancialComplement' && can(P.requisitionsWrite);

  // The Requester keeps their queue (aside) when opening the detail; triage/approval have no personal queue.
  // Aside by role: the Requester sees their own queue; Controllership sees the triage queue; the
  // Approver, the approval queue + unit budget (final approvers land on the decision screen
  // via the redirect above, but keep the aside for whoever only has requisitions:approve).
  const roleAside = can(P.requisitionsWrite) ? (
    <RequesterAside empresaId={req.companyId} />
  ) : can(P.requisitionsTriage) ? (
    <ControllershipAside empresaId={req.companyId} />
  ) : can(P.requisitionsApprove) ? (
    <ApproverAside empresaId={req.companyId} />
  ) : undefined;

  // O painel de decisão encabeça o aside — sticky, continua visível com o Histórico na tela. Sem ações
  // disponíveis ele não entra, para não abrir uma coluna vazia quando o usuário também não tem aside de papel.
  const actionsInAside = hasActions && asideIsSticky;
  const aside = actionsInAside ? (
    <>
      <TransitionActions req={req} />
      {roleAside}
    </>
  ) : (
    roleAside
  );

  return (
    <AsideLayout aside={aside}>
      <BackLink fallback='/app/requisitions' />

      <RequisitionPipeline status={req.status} />

      {/* Panels stacked with uniform vertical rhythm (`.panel` has no margin of its own). */}
      <div className='detail-stack'>
        {/* Return/cancellation reason right below the stepper — the requester reads first why it came back. */}
        {reason && <ReasonCallout action={reason.action} reason={reason.reason} />}

        {/* Fallback do aside desempilhado: primeiro o motivo (por que voltou), depois o que fazer. */}
        {hasActions && !asideIsSticky && <TransitionActions req={req} />}

        {/* Unified Data and Vendor card (redesign). The Vendor block only appears with registry:read. */}
        <RequisitionSummaryCard req={req} headerExtra={<PresenceAvatars scope='requisition' resourceId={req.id} />} />

        {/* Read-only view of the front-loaded financial plan (installments + emission), so the requester
            sees it when the requisition was returned for edits. Hidden while authoring the editable complement
            below (the FinancialComplementForm already shows the editable subform then). Returns null with no plan. */}
        {!authoringComplement && <FinancialPlanView req={req} />}

        {/* Linked project card (AEL-790): accumulated ceiling × consumed × forecast. */}
        {req.projectId && projectMetrics && <ProjectBudgetCard metrics={projectMetrics} />}

        {/* AEL-877 — Conta Fixa in payment: generated titles (base + monthly invoices), post the month's invoice and
          close it. The Requester posts; the other roles only view. */}
        {req.status === 'InPayment' && req.type === 'FixedExpense' && (
          <RecurrenceCyclesPanel req={req} canLaunch={can(P.requisitionsWrite)} />
        )}

        {/* Approver: month-by-month budget decision (server-side control of the requisition). The overage
          is a hard block with no override — EXCEPT Conta Fixa (AEL-881, R1), which can be approved above the ceiling
          (the panel shows the impact, but the Aprovar button is not blocked). */}
        {req.status === 'Approval' && <DecisionBudgetPanel req={req} perCompetencyValue={perCompetencyValue} />}

        {/* Budget forecast: the requester tracks the impact in the pre-approval stages
          (draft/triage/complement/financial complement). */}
        {['Draft', 'Triage', 'Complement', 'FinancialComplement'].includes(req.status) && (
          <Panel title={t('requisitions.forecast.title')}>
            <BudgetForecast
              unitId={req.unitId}
              costCenterId={req.costCenterId}
              initialCompetency={req.startCompetency}
              currentRequestValue={perCompetencyValue}
              excludeRequisitionId={req.id}
              requestedLabel={t('requisitions.forecast.forecast')}
            />
            {/* Recurring/monthly ones cover several competencies — the guard validates ALL of them,
              not just the initial one (AEL-790). Made explicit to avoid supplementing only one month. */}
            {spansCompetencyWindow(req) && (
              <div style={{ padding: '0 16px 8px', fontSize: 12, opacity: 0.75 }}>
                {t('requisitions.forecast.coversWindow', { window: competencyText })}
              </div>
            )}
          </Panel>
        )}

        {/* AEL-844 — submission of the financial information (installments + issuance) by the Requester, right below the
            forecast. The attachments panel becomes the single upload point (label "Documento fiscal ou fatura") and is
            passed as children so the submit button sits BELOW it. */}
        {authoringComplement && (
          <FinancialComplementForm req={req}>
            <AttachmentsPanel
              req={req}
              onDelete={setAttToDelete}
              title={t('requisitions.financialComplement.docsTitle')}
            />
          </FinancialComplementForm>
        )}

        <div className='detail-sections'>
          {/* In the financial complement the attachments already appear inside the form above — avoids the duplicate field.
              Conta Fixa in payment: the general upload stays locked — the NF is uploaded via the "Lançar fatura do mês" form,
              in a single place, going straight to the cycle's Payable (AEL-877). */}
          {!authoringComplement && (
            <AttachmentsPanel
              req={req}
              onDelete={setAttToDelete}
              uploadLocked={req.type === 'FixedExpense' && req.status === 'InPayment'}
            />
          )}
          <Panel title={t('requisitions.detail.timeline')}>
            <AuditTimeline events={transitionsToEvents(req.transitions, me?.userId)} />
          </Panel>
        </div>
      </div>

      {attToDelete && (
        <ConfirmDialog
          title={t('requisitions.attachments.deleteTitle')}
          danger
          busy={deleteAttachment.isPending}
          confirmLabel={t('requisitions.attachments.remove')}
          message={t('requisitions.attachments.deleteMessage', { name: attToDelete.fileName })}
          onConfirm={() =>
            deleteAttachment.mutate(
              { id: req.id, attachmentId: attToDelete.id },
              { onSuccess: () => setAttToDelete(null) },
            )
          }
          onCancel={() => setAttToDelete(null)}
        />
      )}
    </AsideLayout>
  );
}

/** Cada devolução tem seu próprio rótulo: o pedido de ajuste nas parcelas não é a mesma coisa que o
 *  complemento geral, nem que a devolução vinda do Financeiro. */
const LABEL_BY_REASON: Record<ReasonAction, string> = {
  Cancel: 'requisitions.detail.cancelReasonLabel',
  ReturnFromPayment: 'requisitions.detail.financeReturnReasonLabel',
  ReturnToFinancialComplement: 'requisitions.detail.financialReturnReasonLabel',
  ReturnForComplement: 'requisitions.detail.returnReasonLabel',
};

/** Highlighted callout of the return reason (amber) or cancellation (red). */
export function ReasonCallout({ action, reason }: { action: ReasonAction; reason: string }) {
  const { t } = useTranslation();
  const isCancel = action === 'Cancel';
  const Icon = isCancel ? X : CornerUpLeft;
  const color = isCancel ? 'var(--danger)' : 'var(--warning)';
  const bg = isCancel ? 'var(--danger-subtle)' : 'var(--warning-subtle)';
  const label = LABEL_BY_REASON[action];
  return (
    <div
      role='alert'
      style={{
        display: 'flex',
        gap: 10,
        alignItems: 'flex-start',
        padding: '12px 16px',
        borderRadius: 'calc(var(--radius) + 2px)',
        border: `1px solid ${color}`,
        background: bg,
      }}
    >
      <Icon size={18} style={{ color, flexShrink: 0, marginTop: 1 }} />
      <div>
        <div style={{ fontWeight: 600, fontSize: 13, marginBottom: 2 }}>{t(label)}</div>
        <div style={{ fontSize: 14 }}>{reason}</div>
      </div>
    </div>
  );
}
