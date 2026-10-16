import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { type BudgetMonth, type Requisition } from '../../api';
import { useRequisitionBudgetControl } from '../../queries';
import { brl } from '../../lib';
import { DecisionPanel, DecisionRow } from '../../components/workflow';
import { stageSla } from './sla';

/** Estágios em que a tela existe para uma decisão — fora deles o painel é só o conjunto de ações. */
const DECIDING = ['Triage', 'Approval'];

/**
 * Painel de decisão da requisição: a moldura vem de `DecisionPanel`; aqui se decide o que é contexto.
 *
 * Para quem decide (Triagem/Aprovação) carrega o que a decisão exige — saldo do centro de custo, saldo
 * depois desta e SLA. Para o solicitante o painel é só os botões: o impacto dele já está no
 * `BudgetForecast`, na coluna principal, e repetir aqui seria o mesmo número duas vezes na tela.
 */
export function RequisitionDecisionPanel({ req, children }: { req: Requisition; children: ReactNode }) {
  const { t } = useTranslation();
  const deciding = DECIDING.includes(req.status);

  return (
    <DecisionPanel
      title={t('requisitions.decisionPanel.title')}
      subtitle={t('requisitions.decisionPanel.subtitle', { code: req.code, value: brl(req.estimateValue) })}
      context={deciding ? <BudgetContext req={req} /> : undefined}
    >
      {children}
    </DecisionPanel>
  );
}

/**
 * Saldo do centro de custo, saldo depois desta e SLA. O painel mês-a-mês (`DecisionBudgetPanel`) segue na
 * coluna principal com o detalhamento — aqui fica só o veredito, que é o que se lê com o cursor no botão.
 */
function BudgetContext({ req }: { req: Requisition }) {
  const { t } = useTranslation();
  const control = useRequisitionBudgetControl(req.id);
  const month = tightestMonth(control.data?.months ?? []);
  const sla = stageSla(req.statusChangedAt, 24);

  // Uma competência sem orçamento não tem saldo: mostrar "R$ 0,00" ali seria afirmar algo falso.
  const unbudgeted = !!month && !month.budgeted;
  const after = month ? month.available - month.cost : 0;

  return (
    <>
      {month && (
        <>
          <DecisionRow
            label={t('requisitions.decisionPanel.available')}
            value={unbudgeted ? t('requisitions.decisionPanel.unbudgeted') : brl(month.available)}
            tone={unbudgeted ? 'muted' : undefined}
          />
          {!unbudgeted && (
            <DecisionRow
              label={t('requisitions.decisionPanel.afterThis')}
              value={brl(after)}
              tone={after < 0 ? 'bad' : 'warn'}
            />
          )}
        </>
      )}
      {sla && (
        <DecisionRow
          label={t('requisitions.decisionPanel.sla')}
          value={
            sla.overdue
              ? t('requisitions.decisionPanel.slaOverdue', { hours: Math.abs(Math.round(sla.remaining)) })
              : t('requisitions.decisionPanel.slaRemaining', { hours: Math.round(sla.remaining) })
          }
          tone={sla.overdue ? 'bad' : sla.tone === 'warn' ? 'warn' : undefined}
        />
      )}
    </>
  );
}

/**
 * A requisição pode cobrir várias competências; o número que decide é o do mês mais apertado (menor sobra
 * depois do custo), não o do primeiro. Exportado para teste.
 */
export function tightestMonth(months: readonly BudgetMonth[]): BudgetMonth | null {
  if (months.length === 0) return null;
  return months.reduce((worst, m) => (m.available - m.cost < worst.available - worst.cost ? m : worst));
}
