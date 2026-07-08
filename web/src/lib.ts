/** The reserved system-actor Guid stamped on payments created by the auto-pay job. */
export const SYSTEM_ACTOR_ID = '00000000-0000-0000-0000-0000000005ed';

export const brl = (v: number) =>
  v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 });

/** Currency with cents — used by accounting reports where precision matters. */
export const brlCents = (v: number) =>
  v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2 });

/** Rounds to cents (2 decimals), half-away-from-zero — matches the backend's MidpointRounding.AwayFromZero
 *  so the value shown equals the value persisted (numeric(18,2)). Withholding amounts are non-negative. */
export const roundCents = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;

/** Formats a decimal fraction as a pt-BR percent: 0.0065 → "0,65%". */
export const fmtPct = (fraction: number) =>
  fraction.toLocaleString('pt-BR', { style: 'percent', minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** Withholding rate stored as a fraction (0.11) ⇄ percent for entry/display (11). Rounds to 4 decimals to avoid
 *  float noise (0.035 × 100 = 3.5, not 3.4999…). */
export const fractionToPercent = (fraction: number) => Math.round(fraction * 1_000_000) / 10_000;
export const percentToFraction = (percent: number) => percent / 100;

export const brlCompact = (v: number) =>
  v >= 1_000_000
    ? `R$ ${(v / 1_000_000).toLocaleString('pt-BR', { maximumFractionDigits: 2 })}M`
    : v >= 1_000
      ? `R$ ${(v / 1_000).toLocaleString('pt-BR', { maximumFractionDigits: 0 })}K`
      : brl(v);

export function iniciais(nome: string) {
  const p = nome.trim().split(/\s+/);
  return ((p[0]?.[0] ?? '') + (p[1]?.[0] ?? p[0]?.[1] ?? '')).toUpperCase();
}
