import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useDimensionBudgetControl } from '../../queries';
import { brlCents } from '../../lib';
import { Kpi, LoadingState } from '../../ui';
import { clampPct } from './budget-display';
import { currentCompetency, fmtCompetency, shiftCompetency } from './competency';

/**
 * Painel "Previsão de Saldo em Tempo Real": Orçado × Disponível × Após aprovação por competência para uma
 * dimensão (unidade × CC). Os números vêm do controle orçamentário do servidor (AEL-830). O disponível é o
 * REAL (orçado − consumido, só InPayment+); o previsto (fila/pipeline) NÃO reserva o disponível — o pedido
 * em edição (`currentRequestValue`) é sobreposto no cliente só como projeção "após aprovação" (AEL-844).
 */
export function BudgetForecast({
  unitId,
  costCenterId,
  initialCompetency,
  currentRequestValue = 0,
  excludeRequisitionId,
  requestedLabel,
}: {
  unitId: string;
  costCenterId: string;
  initialCompetency?: string;
  /** Contribuição do pedido em edição/aprovação (destacada em âmbar). */
  currentRequestValue?: number;
  /** Ignora esta requisição ao somar (evita contá-la duas vezes). */
  excludeRequisitionId?: string;
  /** Rótulo do 3º tile: "Solicitado" (criação) ou "Previsto" (aprovação). */
  requestedLabel: string;
}) {
  const { t } = useTranslation();
  const [comp, setComp] = useState(initialCompetency || currentCompetency());

  const ready = !!unitId && !!costCenterId;
  const control = useDimensionBudgetControl(
    { unitId, costCenterId, competencyFrom: comp, competencyTo: comp, excludeRequisitionId },
    { enabled: ready },
  );

  if (!ready) {
    return (
      <div className='empty-state' style={{ padding: 24 }}>
        <div className='s'>{t('requisitions.forecast.selectDimensions')}</div>
      </div>
    );
  }

  if (control.isLoading) return <LoadingState />;

  const month = control.data?.months?.[0];
  const orcado = month?.orcado ?? 0;
  const consumed = month?.consumed ?? 0;
  const forecast = month?.forecast ?? 0; // fila (pipeline) — NÃO reserva o disponível (AEL-844)
  const available = month?.available ?? orcado - consumed; // disponível REAL (só InPayment+ consome)
  const requested = currentRequestValue;
  const afterApproval = available - requested; // projeção: quanto o disponível fica se ESTA for aprovada
  const over = requested > 0 && requested > available; // este pedido não cabe no disponível real → adequação
  const pct = (v: number) => (orcado > 0 ? clampPct((v / orcado) * 100) : 0);

  return (
    <div style={{ padding: 16 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12 }}>
        <button
          className='icon-btn'
          aria-label={t('requisitions.forecast.prevMonth')}
          title={t('requisitions.forecast.prevMonth')}
          onClick={() => setComp((c) => shiftCompetency(c, -1))}
        >
          <ChevronLeft size={16} />
        </button>
        <span className='t-mono' style={{ fontWeight: 600, flex: 1, textAlign: 'center' }}>
          {t('requisitions.forecast.competency')}: {fmtCompetency(comp)}
        </span>
        <button
          className='icon-btn'
          aria-label={t('requisitions.forecast.nextMonth')}
          title={t('requisitions.forecast.nextMonth')}
          onClick={() => setComp((c) => shiftCompetency(c, 1))}
        >
          <ChevronRight size={16} />
        </button>
      </div>
      <div className='kpi-grid forecast-kpis'>
        <Kpi label={t('requisitions.forecast.budgeted')} value={brlCents(orcado)} />
        <Kpi label={t('requisitions.forecast.available')} value={brlCents(available)} />
        {requested > 0 ? (
          <Kpi label={t('requisitions.forecast.afterApproval')} value={brlCents(afterApproval)} />
        ) : (
          <Kpi label={requestedLabel} value={brlCents(forecast)} />
        )}
      </div>
      {/* Barra: consumido REAL (verde) + este pedido (âmbar) sobre o orçado; vermelha e cheia quando o pedido
          não cabe no disponível real. O previsto (fila) NÃO entra — não reserva orçamento (AEL-844). */}
      <div
        style={{
          display: 'flex',
          width: '100%',
          height: 8,
          borderRadius: 999,
          overflow: 'hidden',
          background: 'var(--border)',
          marginTop: 12,
        }}
      >
        {over ? (
          <div style={{ width: '100%', background: 'var(--danger)' }} />
        ) : (
          <>
            <div style={{ width: `${pct(consumed)}%`, background: 'var(--success)' }} />
            <div style={{ width: `${pct(requested)}%`, background: 'var(--warning)' }} />
          </>
        )}
      </div>
      {over ? (
        <div className='proj-alert' role='alert' style={{ marginTop: 8 }}>
          <span>{t('requisitions.forecast.exceededAvailable', { amount: brlCents(requested - available) })}</span>
        </div>
      ) : (
        requested > 0 && (
          <div style={{ marginTop: 8, fontSize: 12, opacity: 0.75 }}>
            {t('requisitions.forecast.afterApprovalHint', { amount: brlCents(afterApproval) })}
          </div>
        )
      )}
      {forecast > 0 && (
        <div style={{ marginTop: 6, fontSize: 12, opacity: 0.6 }}>
          {t('requisitions.forecast.queuedNote', { amount: brlCents(forecast) })}
        </div>
      )}
      {month && !month.budgeted && (
        <div style={{ marginTop: 8, fontSize: 12, opacity: 0.7 }}>{t('requisitions.forecast.unbudgeted')}</div>
      )}
    </div>
  );
}
