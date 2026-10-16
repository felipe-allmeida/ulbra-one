/** The reserved system-actor Guid stamped on payments created by the auto-pay job. */
export const SYSTEM_ACTOR_ID = '00000000-0000-0000-0000-0000000005ed';

/** Placeholder for a missing/NaN numeric value — keeps formatters from crashing the page. */
const NO_VALUE = '—';

/** True for values that must not reach `.toLocaleString` (would throw on null/undefined). */
const isBlank = (v: number | null | undefined): v is null | undefined => v == null || Number.isNaN(v);

/** A string value or the em-dash placeholder when it is null/undefined/blank. */
export const dash = (v?: string | null): string => (v != null && v.trim() !== '' ? v : NO_VALUE);

export const brl = (v: number | null | undefined) =>
  isBlank(v)
    ? NO_VALUE
    : v.toLocaleString('pt-BR', {
        style: 'currency',
        currency: 'BRL',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });

/** Currency with cents — used by accounting reports where precision matters. */
export const brlCents = (v: number | null | undefined) =>
  isBlank(v) ? NO_VALUE : v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2 });

/** Rounds to cents (2 decimals), half-away-from-zero — matches the backend's MidpointRounding.AwayFromZero
 *  so the value shown equals the value persisted (numeric(18,2)). Withholding amounts are non-negative. */
export const roundCents = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;

/** Formats a decimal fraction as a pt-BR percent: 0.0065 → "0,65%". */
export const fmtPct = (fraction: number | null | undefined) =>
  isBlank(fraction)
    ? NO_VALUE
    : fraction.toLocaleString('pt-BR', { style: 'percent', minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** Withholding rate stored as a fraction (0.11) ⇄ percent for entry/display (11). Rounds to 4 decimals to avoid
 *  float noise (0.035 × 100 = 3.5, not 3.4999…). */
export const fractionToPercent = (fraction: number) => Math.round(fraction * 1_000_000) / 10_000;
export const percentToFraction = (percent: number) => percent / 100;

/** Parses a user-typed percent (accepts pt-BR comma or dot decimals, e.g. "3,5" or "3.5") to a
 *  number; null when blank or not finite. Prevents comma decimals from silently becoming NaN. */
export const parsePercentInput = (value: string): number | null => {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  const n = Number(trimmed.replace(',', '.'));
  return Number.isFinite(n) ? n : null;
};

/** Parses a user-typed money amount in pt-BR convention to a number; null when blank or not finite.
 *  Canonical input is "1.234,56" ('.' groups thousands, ',' is the decimal). When a comma is present
 *  it is the decimal mark, so we strip the grouping dots and swap ','→'.'. Without a comma the string
 *  is treated as already dot-decimal (tolerates a pasted "1234.56"). Prevents comma amounts → NaN. */
export const parseMoneyInput = (value: string): number | null => {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  const normalized = trimmed.includes(',') ? trimmed.replace(/\./g, '').replace(',', '.') : trimmed;
  const n = Number(normalized);
  return Number.isFinite(n) ? n : null;
};

/** Formats a number as the canonical pt-BR money string "1.234,56" (no currency symbol) — for seeding a
 *  CurrencyInput's initial/prefilled value so it shows in the expected format before the user edits it. */
export const moneyInput = (v: number): string =>
  v.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export const brlCompact = (v: number | null | undefined) =>
  isBlank(v)
    ? NO_VALUE
    : v >= 1_000_000
      ? `R$ ${(v / 1_000_000).toLocaleString('pt-BR', { maximumFractionDigits: 2 })}M`
      : v >= 1_000
        ? `R$ ${(v / 1_000).toLocaleString('pt-BR', { maximumFractionDigits: 0 })}K`
        : brl(v);

export function iniciais(nome: string) {
  const p = nome.trim().split(/\s+/);
  return ((p[0]?.[0] ?? '') + (p[1]?.[0] ?? p[0]?.[1] ?? '')).toUpperCase();
}
