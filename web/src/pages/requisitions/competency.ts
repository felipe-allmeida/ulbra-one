import { type Requisition } from '../../api';

/** Formata uma competência "YYYY-MM" como "MM/YYYY" (ou "—" quando ausente). */
export function fmtCompetency(comp: string | null | undefined): string {
  if (!comp) return '—';
  const m = /^(\d{4})-(\d{2})$/.exec(comp);
  return m ? `${m[2]}/${m[1]}` : comp;
}

/**
 * True quando a requisição abrange MAIS de uma competência (janela), não só o mês
 * inicial: Conta fixa (FixedExpense) abrange sempre; qualquer tipo com
 * estimativa Mensal também. Integral (Full) em tipo não-recorrente fica só na competência
 * inicial. Fonte ÚNICA do predicado — espelha `Requisition.AppliesToCompetency` no domínio
 * e é reusada pelo guard orçamentário e pela previsão (budget-guard / budget-math).
 */
export function spansCompetencyWindow(req: Pick<Requisition, 'type' | 'estimateType'>): boolean {
  return req.type === 'FixedExpense' || req.estimateType === 'Monthly';
}

/**
 * Texto da janela de competências que a requisição realmente cobre — a MESMA janela que o
 * guard orçamentário (AEL-790) valida, não apenas o mês inicial. Mês único → "07/2026";
 * janela fechada → "07/2026–12/2026"; recorrência aberta → "07/2026 → {recurringLabel}".
 */
export function competencyWindowText(
  req: Pick<Requisition, 'type' | 'estimateType' | 'startCompetency' | 'endCompetency'>,
  recurringLabel: string,
): string {
  const start = fmtCompetency(req.startCompetency);
  if (!spansCompetencyWindow(req)) return start;
  if (req.endCompetency) return `${start}–${fmtCompetency(req.endCompetency)}`;
  return `${start} → ${recurringLabel}`;
}

/** Competência "YYYY-MM" do mês atual (hora local). */
export function currentCompetency(now: Date = new Date()): string {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

/** Desloca uma competência "YYYY-MM" por `months` (pode ser negativo). */
export function shiftCompetency(comp: string, months: number): string {
  const m = /^(\d{4})-(\d{2})$/.exec(comp);
  if (!m) return comp;
  const d = new Date(Number(m[1]), Number(m[2]) - 1 + months, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

/** Número de meses no intervalo inclusivo [start, end] ("YYYY-MM"); 0 quando inválido ou end < start. */
export function monthsBetween(start: string, end: string): number {
  const s = /^(\d{4})-(\d{2})$/.exec(start);
  const e = /^(\d{4})-(\d{2})$/.exec(end);
  if (!s || !e) return 0;
  const months = (Number(e[1]) - Number(s[1])) * 12 + (Number(e[2]) - Number(s[2])) + 1;
  return months < 1 ? 0 : months;
}
