import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Ban, Check, ChevronLeft, ClipboardCheck, CornerUpLeft } from 'lucide-react';
import { type Requisition } from '../../api';
import {
  useApproveRequisition,
  useCancelRequisition,
  useMe,
  useProjectMetrics,
  useRequisition,
  useReturnRequisitionForComplement,
  useReturnRequisitionToFinancialComplement,
} from '../../queries';
import { P, usePermissions } from '../../permissions';
import { useMediaQuery } from '../../useMediaQuery';
import { Button, ConfirmDialog, EmptyState, Panel, Textarea, LoadingState } from '../../ui';
import { useToast } from '../../toast';
import { ApiError, getErrorMessage } from '../../apiError';
import { AuditTimeline, AsideLayout, type AuditEvent } from '../../components/workflow';
import { scrollContentToTop, useBackNavigation } from '../../nav';
import { stageSla } from './sla';
import { RequisitionPipeline } from './pipeline';
import { AttachmentsPanel } from './attachments-panel';
import { DecisionBudgetPanel } from './decision-budget';
import { ProjectBudgetCard } from './project-budget-card';
import { RequisitionSummaryCard } from './summary-card';
import { RequisitionDecisionPanel } from './decision-panel';
import { ApproverAside } from './approver-aside';
import { ReasonCallout } from './detail';
import { useBudgetIssues } from './budget-guard';
import { monthsBetween, spansCompetencyWindow } from './competency';
import { activeReason, transitionsToEvents } from './timeline';

/**
 * Approver decision screen (AEL-859, mock "sup detail"): everything the approver needs to read
 * before deciding, in the mock's order — month-by-month budget decision, project, data, attachments and
 * history — with the Aprovar / Devolver / Recusar actions in the footer. Outside `Approval` it is read-only
 * (the inbox "Todas"/"Histórico" rows open here too).
 */
export function RequisitionApprovalDecisionPage() {
  const { id } = useParams();
  const { t } = useTranslation();
  const nav = useNavigate();
  const { to: backTo, goBack, label: backLabel } = useBackNavigation('/app/requisitions/approval');
  const { can } = usePermissions();
  const query = useRequisition(id);
  const req = query.data ?? null;
  const me = useMe().data;
  const projectMetrics = useProjectMetrics(req?.projectId ?? undefined).data;
  const asideIsSticky = useMediaQuery('(min-width: 1200px)');

  if (query.isLoading) return <LoadingState />;
  if (!req) return <EmptyState icon={<ClipboardCheck />} title={t('requisitions.empty.notFound')} />;

  const awaiting = req.status === 'Approval';
  const sla = awaiting ? stageSla(req.statusChangedAt, 24) : null;
  const reason = activeReason(req);

  // EstimateValue is the TOTAL; uniform per-competency approximation (the server sends the exact cost).
  const windowMonths = req.endCompetency ? monthsBetween(req.startCompetency, req.endCompetency) : 1;
  const perCompetencyValue =
    spansCompetencyWindow(req) && windowMonths > 1 ? req.estimateValue / windowMonths : req.estimateValue;

  // Synthetic "Aguardando sua aprovação" event closes the timeline while the decision is with the user.
  const events: AuditEvent[] = transitionsToEvents(req.transitions, me?.userId);
  if (awaiting) {
    events.push({
      type: 'AwaitingYourApproval',
      actorName: t('requisitions.timeline.you'),
      timestamp: req.statusChangedAt,
    });
  }

  // Painel de decisão: encabeça o aside sticky acima de 1200px; abaixo disso o aside desempilha para o
  // fim da página, então ele sobe para o topo da coluna única.
  // Mesma condição de `active` dentro do componente de ações — a página precisa dela para decidir
  // ONDE renderizar, e para não abrir um aside vazio quando não há nem decisão nem fila.
  const canDecide = can(P.requisitionsApprove) && req.status === 'Approval';
  const hasRoleAside = can(P.requisitionsApprove);
  const decisionActions = (
    <ApprovalDecisionActions
      req={req}
      onDecided={() => {
        nav(backTo);
        scrollContentToTop();
      }}
    />
  );

  return (
    <AsideLayout
      aside={
        (asideIsSticky && canDecide) || hasRoleAside ? (
          <>
            {asideIsSticky && canDecide && decisionActions}
            {hasRoleAside && <ApproverAside empresaId={req.companyId} projectionReqId={req.id} />}
          </>
        ) : undefined
      }
    >
      <button className='backlink' onClick={goBack}>
        <ChevronLeft /> {backLabel}
      </button>

      {sla?.overdue && (
        <div className='proj-alert' role='alert' style={{ marginBottom: 12 }}>
          <span>{t('requisitions.decisionScreen.lateBanner', { hours: -sla.remaining })}</span>
        </div>
      )}

      <RequisitionPipeline status={req.status} />

      <div className='detail-stack'>
        {!asideIsSticky && canDecide && decisionActions}

        {reason && <ReasonCallout action={reason.action} reason={reason.reason} />}

        {/* Unified card (redesign). The Approver decides only with the requisition data — no Vendor. */}
        <RequisitionSummaryCard req={req} showVendor={false} />

        {awaiting && <DecisionBudgetPanel req={req} perCompetencyValue={perCompetencyValue} />}

        {req.projectId && projectMetrics && <ProjectBudgetCard metrics={projectMetrics} />}

        <div className='detail-sections'>
          {/* Without `requisitions:write` the attachments panel is already read-only (view/download only). */}
          <AttachmentsPanel req={req} onDelete={() => undefined} />
          <Panel title={t('requisitions.detail.timeline')}>
            <AuditTimeline events={events} />
          </Panel>
        </div>
      </div>
    </AsideLayout>
  );
}

/**
 * Final approver decision actions — Aprovar (blocked on overage, no override), Devolver
 * (optional reason) and Recusar (= cancellation, reason REQUIRED). Reused by "Revisar em
 * sequência"; `onDecided` notifies the caller of success (navigate back / advance the queue).
 */
export function ApprovalDecisionActions({
  req,
  onDecided,
  onConflict,
}: {
  req: Requisition;
  onDecided?: () => void;
  /** Sequence: requisition already decided by someone else (409 InvalidTransition) → skip instead of blocking. */
  onConflict?: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const { can } = usePermissions();
  const canApprove = can(P.requisitionsApprove);

  const approve = useApproveRequisition();
  const returnForComplement = useReturnRequisitionForComplement();
  const returnToFinancial = useReturnRequisitionToFinancialComplement();
  const cancel = useCancelRequisition();
  const [returnOpen, setReturnOpen] = useState(false);
  const [returnFinancialOpen, setReturnFinancialOpen] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [reason, setReason] = useState('');
  const onError = (e: unknown) => {
    if (onConflict && e instanceof ApiError && e.status === 409 && e.errorCode === 'requisition.invalid_transition') {
      toast.error(t('requisitions.review.conflictSkipped'));
      setReturnOpen(false);
      setReturnFinancialOpen(false);
      setRejectOpen(false);
      onConflict();
      return;
    }
    toast.error(getErrorMessage(e));
  };

  const active = canApprove && req.status === 'Approval';
  // Authoritative overage (competency breach or project ceiling): Aprovar is disabled and the
  // path is the Adequação Orçamentária, done by Controllership — no dead button here (AEL-859).
  const issues = useBudgetIssues(req, active);
  if (!active) return null;

  return (
    <RequisitionDecisionPanel req={req}>
      {issues.hasIssues && (
        <div className='proj-alert' role='alert'>
          <span>{t('requisitions.decisionScreen.needsAdjustment')}</span>
        </div>
      )}
      <Button
        className='btn-approve'
        icon={<Check />}
        disabled={approve.isPending || issues.loading || issues.hasIssues}
        onClick={() =>
          approve.mutate(
            { id: req.id },
            {
              onError,
              onSuccess: () => {
                toast.success(t('requisitions.actions.approved'));
                onDecided?.();
              },
            },
          )
        }
      >
        {t('requisitions.actions.approve')}
      </Button>
      <Button
        icon={<CornerUpLeft />}
        onClick={() => {
          setReason('');
          setReturnOpen(true);
        }}
      >
        {t('requisitions.actions.return')}
      </Button>
      <Button
        icon={<CornerUpLeft />}
        onClick={() => {
          setReason('');
          setReturnFinancialOpen(true);
        }}
      >
        {t('requisitions.actions.returnToFinancial')}
      </Button>
      <Button
        variant='danger-outline'
        icon={<Ban />}
        onClick={() => {
          setReason('');
          setRejectOpen(true);
        }}
      >
        {t('requisitions.decisionScreen.reject')}
      </Button>

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
                  onDecided?.();
                },
              },
            )
          }
          onCancel={() => setReturnOpen(false)}
        />
      )}

      {returnFinancialOpen && (
        <ConfirmDialog
          title={t('requisitions.returnFinancial.title')}
          confirmLabel={t('requisitions.returnFinancial.confirm')}
          busy={returnToFinancial.isPending}
          confirmDisabled={!reason.trim()}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('requisitions.returnFinancial.message')}</div>
              <Textarea
                autoFocus
                value={reason}
                placeholder={t('requisitions.returnFinancial.placeholder')}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            returnToFinancial.mutate(
              { id: req.id, reason: reason.trim() || undefined },
              {
                onError,
                onSuccess: () => {
                  setReturnFinancialOpen(false);
                  toast.success(t('requisitions.actions.returnedToFinancial'));
                  onDecided?.();
                },
              },
            )
          }
          onCancel={() => setReturnFinancialOpen(false)}
        />
      )}

      {rejectOpen && (
        <ConfirmDialog
          title={t('requisitions.decisionScreen.rejectTitle')}
          danger
          confirmLabel={t('requisitions.decisionScreen.rejectConfirm')}
          busy={cancel.isPending}
          confirmDisabled={!reason.trim()}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('requisitions.decisionScreen.rejectMessage')}</div>
              <Textarea
                autoFocus
                value={reason}
                placeholder={t('requisitions.decisionScreen.rejectPlaceholder')}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            cancel.mutate(
              { id: req.id, reason: reason.trim() },
              {
                onError,
                onSuccess: () => {
                  setRejectOpen(false);
                  toast.success(t('requisitions.decisionScreen.rejected'));
                  onDecided?.();
                },
              },
            )
          }
          onCancel={() => setRejectOpen(false)}
        />
      )}
    </RequisitionDecisionPanel>
  );
}
