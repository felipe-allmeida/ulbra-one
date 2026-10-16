import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Check, ChevronRight, SkipForward, X } from 'lucide-react';
import { type Requisition } from '../../api';
import { useProjects, useRequisitions, useUnits, useVendor } from '../../queries';
import { Button, Panel, LoadingState } from '../../ui';
import { DecisionBudgetPanel } from './decision-budget';
import { TriageDecisionActions } from './triage-decision';
import { competencyWindowText, monthsBetween, spansCompetencyWindow } from './competency';

/**
 * Triage "Revisar em sequência" — fullscreen overlay that walks the TRIAGE queue one by one with inline
 * decisions. Mirrors Approver's (AEL-859): queue FROZEN on open (snapshot), Esc closes, an item
 * already triaged by someone else (409) is skipped. Deep vendor check is one click away ("Abrir decisão
 * completa"); here the focus is deciding fast.
 */
export function TriageSequence({ companyId, onClose }: { companyId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const list = useRequisitions(
    { companyId, status: 'Triage', mine: false, page: 1, pageSize: 50 },
    { enabled: !!companyId },
  );
  const [snapshot, setSnapshot] = useState<Requisition[] | null>(null);
  const [idx, setIdx] = useState(0);
  const [decided, setDecided] = useState(0);
  const [skipped, setSkipped] = useState(0);

  useEffect(() => {
    if (!snapshot && list.data) setSnapshot(list.data.items);
  }, [snapshot, list.data]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const items = snapshot ?? [];
  const total = items.length;
  const current = idx < total ? items[idx] : null;
  const finished = snapshot !== null && current === null;

  return (
    <div className='seq' role='dialog' aria-modal='true' aria-label={t('requisitions.review.subtitleTriage')}>
      <div className='seqbar'>
        <div>
          <div className='prog'>
            {finished
              ? t('requisitions.review.doneProgress')
              : t('requisitions.review.progress', { i: Math.min(idx + 1, Math.max(total, 1)), n: total })}
          </div>
          <div className='progsub'>{t('requisitions.review.subtitleTriage')}</div>
        </div>
        <button
          className='icon-btn'
          style={{ marginLeft: 'auto' }}
          aria-label={t('requisitions.review.close')}
          title={t('requisitions.review.close')}
          onClick={onClose}
        >
          <X />
        </button>
      </div>
      <div className='seqtrack'>
        <div className='seqfill' style={{ width: `${total > 0 ? Math.min(idx / total, 1) * 100 : 0}%` }} />
      </div>
      <div className='seqscroll'>
        <div className='seqinner'>
          {!snapshot ? (
            <LoadingState />
          ) : finished ? (
            <div className='seqdone'>
              <div className='big'>
                <Check size={30} />
              </div>
              <h1 className='seqname'>{t('requisitions.review.doneTitle')}</h1>
              <p className='seqmeta'>
                {total === 0
                  ? t('requisitions.review.empty')
                  : skipped > 0
                    ? t('requisitions.review.doneSubWithSkipped', { decided, skipped })
                    : t('requisitions.review.doneSubDecided', { count: decided })}
              </p>
              <Button variant='brand' onClick={onClose}>
                {t('requisitions.review.backToQueue')}
              </Button>
            </div>
          ) : (
            <SequenceItem
              key={current!.id}
              req={current!}
              companyId={companyId}
              onDecided={() => {
                setDecided((d) => d + 1);
                setIdx((i) => i + 1);
              }}
              onSkip={() => {
                setSkipped((s) => s + 1);
                setIdx((i) => i + 1);
              }}
            />
          )}
        </div>
      </div>
    </div>
  );
}

function SequenceItem({
  req,
  companyId,
  onDecided,
  onSkip,
}: {
  req: Requisition;
  companyId: string;
  onDecided: () => void;
  onSkip: () => void;
}) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const vendor = useVendor(req.vendorId).data;
  const units = useUnits(companyId).data ?? [];
  const projects = useProjects(companyId).data ?? [];

  const meta = [
    `#${req.code}`,
    t(`requisitions.type.${req.type}`),
    units.find((u) => u.id === req.unitId)?.acronym,
    req.projectId ? projects.find((p) => p.id === req.projectId)?.name : undefined,
    competencyWindowText(req, t('requisitions.competencyWindow.recurring')),
  ]
    .filter(Boolean)
    .join(' · ');
  const windowMonths = req.endCompetency ? monthsBetween(req.startCompetency, req.endCompetency) : 1;
  const perCompetencyValue =
    spansCompetencyWindow(req) && windowMonths > 1 ? req.estimateValue / windowMonths : req.estimateValue;

  return (
    <>
      <h1 className='seqname'>{vendor?.name ?? `#${req.code}`}</h1>
      <p className='seqmeta'>
        {meta} · {req.description}
      </p>
      <Panel title={t('requisitions.decision.title')}>
        <DecisionBudgetPanel req={req} perCompetencyValue={perCompetencyValue} />
      </Panel>
      <button
        className='backlink'
        style={{ margin: '10px 0 0' }}
        onClick={() => nav(`/app/requisitions/triage/${req.id}`, { state: { from: '/app/requisitions/triage' } })}
      >
        {t('requisitions.review.openFull')} <ChevronRight size={15} />
      </button>
      <div style={{ marginTop: 14 }}>
        <TriageDecisionActions req={req} onDecided={onDecided} onConflict={onSkip} />
        <Button icon={<SkipForward />} onClick={onSkip} style={{ marginTop: 8 }}>
          {t('requisitions.review.skip')}
        </Button>
      </div>
    </>
  );
}
