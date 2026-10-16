import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ArrowRight, ChevronLeft, ClipboardCheck, CornerUpLeft, Scale, X } from 'lucide-react';
import { type Requisition } from '../../api';
import {
  useApproveTriage,
  useCancelRequisition,
  useMe,
  useProjectMetrics,
  useRequisition,
  useRequisitionBudgetControl,
  useReturnRequisitionForComplement,
} from '../../queries';
import { P, usePermissions } from '../../permissions';
import { Button, ConfirmDialog, EmptyState, Panel, Textarea, LoadingState } from '../../ui';
import { useToast } from '../../toast';
import { ApiError, getErrorMessage } from '../../apiError';
import { AuditTimeline, RoleHome, type AuditEvent } from '../../components/workflow';
import { useBackNavigation } from '../../nav';
import { stageSla } from './sla';
import { RequisitionPipeline } from './pipeline';
import { AttachmentsPanel } from './attachments-panel';
import { DecisionBudgetPanel } from './decision-budget';
import { ProjectBudgetCard } from './project-budget-card';
import { RequisitionSummaryCard } from './summary-card';
import { FinancialPlanView } from './financial-plan-view';
import { ControllershipAside } from './controllership-aside';
import { useBudgetIssues } from './budget-guard';
import { monthsBetween, spansCompetencyWindow } from './competency';
import { latestReason, retriageReason, transitionsToEvents, triageApprovedToastKey } from './timeline';
import { ReasonCallout } from './detail';

/**
 * Controllership decision screen (Entrega 4, mock "ctrl detail"): validates the requisition at triage. The
 * central artifact is the vendor check inside the unified summary card (RequisitionSummaryCard); the budget is
 * informational (it does NOT block — an overage becomes a Budget Adjustment). Actions: forward to approval /
 * return / reject / request adjustment. Read-only outside `Triage` (the inbox's "Todas"/"Histórico" rows open here too).
 */
export function RequisitionTriageDecisionPage() {
  const { id } = useParams();
  const { t } = useTranslation();
  const nav = useNavigate();
  const { to: backTo, goBack, label: backLabel } = useBackNavigation('/app/requisitions/triage');
  const { can } = usePermissions();
  const query = useRequisition(id);
  const req = query.data ?? null;
  const me = useMe().data;
  const projectMetrics = useProjectMetrics(req?.projectId ?? undefined).data;

  if (query.isLoading) return <LoadingState />;
  if (!req) return <EmptyState icon={<ClipboardCheck />} title={t('requisitions.empty.notFound')} />;

  const awaiting = req.status === 'Triage';
  const sla = awaiting ? stageSla(req.statusChangedAt, 24) : null;
  const retriage = retriageReason(req);
  // Return reason (incl. a Finance return to controllership — AEL-935) shown as the amber callout, same as the
  // requester/approver screens. retriage below is the distinct "value/vendor revalue" notice.
  const reason = latestReason(req);

  // EstimateValue is the TOTAL; uniform per-competency approximation (the server sends the exact cost).
  const windowMonths = req.endCompetency ? monthsBetween(req.startCompetency, req.endCompetency) : 1;
  const perCompetencyValue =
    spansCompetencyWindow(req) && windowMonths > 1 ? req.estimateValue / windowMonths : req.estimateValue;

  const events: AuditEvent[] = transitionsToEvents(req.transitions, me?.userId);
  if (awaiting) {
    events.push({
      type: 'AwaitingYourTriage',
      actorName: t('requisitions.timeline.you'),
      timestamp: req.statusChangedAt,
    });
  }

  return (
    <RoleHome
      aside={
        can(P.requisitionsTriage) ? (
          <ControllershipAside empresaId={req.companyId} projectionReqId={req.id} />
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
        {reason && <ReasonCallout action={reason.action} reason={reason.reason} />}

        {retriage && (
          <div className='proj-alert' role='status'>
            <span>
              <strong>{t('requisitions.triageDecision.retriageTitle')}</strong> —{' '}
              {t('requisitions.triageDecision.retriageBody')}
              {retriage.reason ? ` (${retriage.reason})` : ''}
            </span>
          </div>
        )}

        {/* Card unificado (redesign): Dados + Fornecedor — a verificação do fornecedor é o artefato central da triagem. */}
        <RequisitionSummaryCard req={req} />

        {/* Quando o solicitante já informou a parte financeira (front-load), mostra o plano em
            somente-leitura para a análise de triagem (parcelas + data de emissão). */}
        <FinancialPlanView req={req} />

        {awaiting && <DecisionBudgetPanel req={req} perCompetencyValue={perCompetencyValue} />}

        {req.projectId && projectMetrics && <ProjectBudgetCard metrics={projectMetrics} />}

        <div className='detail-sections'>
          <AttachmentsPanel req={req} onDelete={() => undefined} />
          <Panel title={t('requisitions.detail.timeline')}>
            <AuditTimeline events={events} />
          </Panel>
        </div>

        <TriageDecisionActions req={req} onDecided={() => nav(backTo)} />
      </div>
    </RoleHome>
  );
}

/**
 * Triage decision actions — forward to approval (ApproveTriage; NEVER blocked by an overage), return for
 * fixes (optional reason), reject (= cancel, MANDATORY reason) and request adjustment (when over budget,
 * with prefill). Reused by "Revisar em sequência"; `onDecided` notifies the caller of success.
 */
export function TriageDecisionActions({
  req,
  onDecided,
  onConflict,
}: {
  req: Requisition;
  onDecided?: () => void;
  /** Sequence: requisition already triaged by someone else (409 InvalidTransition) → skip instead of blocking. */
  onConflict?: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const nav = useNavigate();
  const { can } = usePermissions();
  const canTriage = can(P.requisitionsTriage);

  const approveTriage = useApproveTriage();
  const returnForComplement = useReturnRequisitionForComplement();
  const cancel = useCancelRequisition();
  const [returnOpen, setReturnOpen] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [reason, setReason] = useState('');
  const onError = (e: unknown) => {
    if (onConflict && e instanceof ApiError && e.status === 409 && e.errorCode === 'requisition.invalid_transition') {
      toast.error(t('requisitions.review.conflictSkipped'));
      setReturnOpen(false);
      setRejectOpen(false);
      onConflict();
      return;
    }
    toast.error(getErrorMessage(e));
  };

  const active = canTriage && req.status === 'Triage';
  // An overage does NOT block triage (unlike final approval): just a hint + a Budget Adjustment shortcut.
  const issues = useBudgetIssues(req, active);
  const control = useRequisitionBudgetControl(req.id, { enabled: active });
  if (!active) return null;

  const breach = control.data?.breaches[0];
  const openAdjustment = () => {
    const q = new URLSearchParams({ costCenterId: req.costCenterId, unitId: req.unitId });
    q.set('competency', breach?.competency ?? req.startCompetency);
    if (breach) q.set('value', String(Math.max(0, breach.cost - breach.available)));
    nav(`/app/budget-adjustments/new?${q}`);
  };

  return (
    <div>
      {issues.hasIssues && (
        <div className='proj-alert' role='alert' style={{ marginBottom: 10 }}>
          <span>{t('requisitions.triageDecision.overBudgetHint')}</span>
        </div>
      )}
      <div className='detail-actions detail-actions--end'>
        <Button
          className='btn-approve'
          icon={<ArrowRight />}
          disabled={approveTriage.isPending}
          onClick={() =>
            approveTriage.mutate(
              { id: req.id },
              {
                onError,
                onSuccess: (updated) => {
                  toast.success(t(triageApprovedToastKey(updated.status)));
                  onDecided?.();
                },
              },
            )
          }
        >
          {t('requisitions.triageDecision.forward')}
        </Button>
        <Button
          icon={<CornerUpLeft />}
          onClick={() => {
            setReason('');
            setReturnOpen(true);
          }}
        >
          {t('requisitions.triageDecision.return')}
        </Button>
        <Button
          variant='danger-outline'
          icon={<X />}
          onClick={() => {
            setReason('');
            setRejectOpen(true);
          }}
        >
          {t('requisitions.decisionScreen.reject')}
        </Button>
        {issues.hasIssues && (
          <Button icon={<Scale />} onClick={openAdjustment}>
            {t('requisitions.triageDecision.requestAdjustment')}
          </Button>
        )}
      </div>

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
                  onDecided?.();
                },
              },
            )
          }
          onCancel={() => setReturnOpen(false)}
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
    </div>
  );
}
