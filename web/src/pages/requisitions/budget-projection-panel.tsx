import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { LineChart } from 'lucide-react';
import { useQueueBudgetProjection, useRequisitionBudgetControl } from '../../queries';
import { brl } from '../../lib';
import { BudgetProjectionChart, type ProjectionPoint } from './budget-projection-chart';
import { currentCompetency, shiftCompetency } from './competency';

/** `.spanel` "Impacto no orçamento" com número grande + sparkline. Não renderiza sem ≥2 pontos. */
function ProjectionPanelView({
  points,
  breaches,
  total,
  sub,
  breachCount,
  legend,
  ariaLabel,
}: {
  points: ProjectionPoint[];
  breaches: string[];
  total: number;
  sub: string;
  breachCount: number;
  legend: { ceiling: string; consumption: string; over: string };
  ariaLabel: string;
}): ReactNode {
  const { t } = useTranslation();
  if (points.length < 2) return null;
  return (
    <div className='spanel'>
      <div className='sph'>
        <div className='spt'>
          <LineChart aria-hidden />
          {t('requisitions.projection.title')}
        </div>
        <div className='sps'>{sub}</div>
      </div>
      <div className='spbody'>
        <div className='bpj-big'>
          <span className='bpj-num'>{brl(total)}</span>
          {breachCount > 0 && (
            <span className='bpj-flag'>{t('requisitions.projection.breachFlag', { count: breachCount })}</span>
          )}
        </div>
        <BudgetProjectionChart points={points} breaches={breaches} ariaLabel={ariaLabel} legend={legend} />
      </div>
    </div>
  );
}

/** Agregado da fila inteira (telas de Requisição/inbox): consumo da unidade × teto. */
export function QueueProjectionPanel({ companyId, unitId }: { companyId: string; unitId?: string }) {
  const { t } = useTranslation();
  const from = currentCompetency();
  const to = shiftCompetency(from, 11);
  const proj = useQueueBudgetProjection(companyId, { unitId, from, to }, { enabled: !!companyId }).data;
  const breachSet = new Set(proj?.breaches ?? []);
  const points = (proj?.months ?? [])
    .filter((m) => m.budgeted || m.forecast > 0 || breachSet.has(m.competency))
    .map((m) => ({
      competency: m.competency,
      ceiling: m.orcado,
      consumed: m.budgeted ? m.consumed + m.forecast : 0,
      budgeted: m.budgeted,
    }));
  return (
    <ProjectionPanelView
      points={points}
      breaches={proj?.breaches ?? []}
      total={proj?.queueTotal ?? 0}
      sub={t('requisitions.projection.queueSub')}
      breachCount={proj?.breaches.length ?? 0}
      legend={{
        ceiling: t('requisitions.projection.legendCeilingUnit'),
        consumption: t('requisitions.projection.legendQueue'),
        over: t('requisitions.projection.legendOver'),
      }}
      ariaLabel={t('requisitions.projection.ariaQueue')}
    />
  );
}

/** Por centro de custo da requisição (tela de decisão): consumo do CC × teto, se aprovar este pedido. */
export function RequisitionProjectionPanel({ reqId }: { reqId: string }) {
  const { t } = useTranslation();
  const control = useRequisitionBudgetControl(reqId).data;
  // À vista de 1 competência só (compra pontual): o gráfico vira um "V" sem valor de análise — a Decisão
  // orçamentária ao lado já cobre. Só renderiza quando o pedido toca ≥2 competências (mensal/recorrência).
  if ((control?.months ?? []).filter((m) => m.cost > 0).length <= 1) return null;
  const breachSet = new Set((control?.breaches ?? []).map((b) => b.competency));
  // Mostra o horizonte inteiro do CC-Unidade — todas as competências ORÇADAS (registradas), não só os meses
  // que o pedido toca. Assim uma compra de 1 mês ainda desenha o consumo do CC até o fim do ano fiscal (as
  // células de orçamento). `cost>0`/breach garantem os meses do pedido mesmo se sem orçamento.
  const months = (control?.months ?? []).filter((m) => m.budgeted || m.cost > 0 || breachSet.has(m.competency));
  const points = months.map((m) => ({
    competency: m.competency,
    ceiling: m.orcado,
    consumed: m.budgeted ? m.consumed + m.cost : 0,
    budgeted: m.budgeted,
  }));
  const total = months.reduce((s, m) => s + m.cost, 0);
  // Só meses ORÇADOS viram ponto vermelho (mês sem orçamento aparece como vértice vazado).
  const chartBreaches = months.filter((m) => m.budgeted && breachSet.has(m.competency)).map((m) => m.competency);
  return (
    <ProjectionPanelView
      points={points}
      breaches={chartBreaches}
      total={total}
      sub={t('requisitions.projection.reqSub')}
      breachCount={chartBreaches.length}
      legend={{
        ceiling: t('requisitions.projection.legendCeilingCc'),
        consumption: t('requisitions.projection.legendReq'),
        over: t('requisitions.projection.legendOver'),
      }}
      ariaLabel={t('requisitions.projection.ariaReq')}
    />
  );
}
