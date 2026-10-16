import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Check, ChevronRight, SkipForward, X } from 'lucide-react';
import { type Requisition } from '../../api';
import { useProjects, useRequisitions, useUnits, useVendor } from '../../queries';
import { Button, Panel, LoadingState } from '../../ui';
import { DecisionBudgetPanel } from './decision-budget';
import { ApprovalDecisionActions } from './approval-decision';
import { competencyWindowText, monthsBetween, spansCompetencyWindow } from './competency';

/**
 * "Revisar em sequência" (AEL-859, mock ".seq"): overlay fullscreen que percorre a fila de aprovação
 * uma a uma com decisão inline. A fila é CONGELADA na abertura (snapshot) — as decisões invalidam as
 * queries por trás sem re-embaralhar o "N de M". Item já decidido por outra pessoa (409) é pulado
 * automaticamente; item estourado tem o Aprovar bloqueado e sai pelo "Pular".
 */
export function ApprovalSequence({ companyId, onClose }: { companyId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const list = useRequisitions(
    { companyId, status: 'Approval', mine: false, page: 1, pageSize: 50 },
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
    <div className='seq' role='dialog' aria-modal='true' aria-label={t('requisitions.review.subtitle')}>
      <div className='seqbar'>
        <div>
          <div className='prog'>
            {finished
              ? t('requisitions.review.doneProgress')
              : t('requisitions.review.progress', { i: Math.min(idx + 1, Math.max(total, 1)), n: total })}
          </div>
          <div className='progsub'>{t('requisitions.review.subtitle')}</div>
        </div>
        <button
          className='icon-btn'
          style={{ marginLeft: 'auto' }}
          aria-label={t('requisitions.review.close')}
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
              // key por requisição: remonta o item (e reseta o cursor de mês do DecisionBudgetPanel)
              // a cada avanço — sem isso o idx do painel vazaria de uma requisição para a próxima.
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
        onClick={() => nav(`/app/requisitions/approval/${req.id}`, { state: { from: '/app/requisitions/approval' } })}
      >
        {t('requisitions.review.openFull')} <ChevronRight size={15} />
      </button>
      <div style={{ marginTop: 14 }}>
        <ApprovalDecisionActions req={req} onDecided={onDecided} onConflict={onSkip} />
        <Button icon={<SkipForward />} onClick={onSkip} style={{ marginTop: 8 }}>
          {t('requisitions.review.skip')}
        </Button>
      </div>
    </>
  );
}
