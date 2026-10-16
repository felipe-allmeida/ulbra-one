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

/** Magnitude steps for abbreviated money, from the largest down (first match wins). Thousands are
 *  deliberately absent here: a KPI tile still fits "R$ 42.500,00", and "R$ 42,5 mil" would drop the
 *  cents for no gain — only `brlCompact`, for chart axes, abbreviates that low. */
const MAGNITUDES = [
  { unit: 1_000_000_000_000, suffix: ' tri', short: 'T' },
  { unit: 1_000_000_000, suffix: ' bi', short: 'B' },
  { unit: 1_000_000, suffix: ' mi', short: 'M' },
] as const;

/** Formats |v| >= 1 million as "R$ 1,25 mi" (or "R$ 1,25M" when `short`, for chart axes). The sign goes
 *  BEFORE "R$" to match what `Intl` does for pt-BR currency ("-R$ 1.234,00"), so a negative never
 *  reads as "R$ -1,25 mi". */
const abbreviate = (v: number, short = false) => {
  const abs = Math.abs(v);
  const m = MAGNITUDES.find((x) => abs >= x.unit) ?? MAGNITUDES[MAGNITUDES.length - 1];
  const mantissa = (abs / m.unit).toLocaleString('pt-BR', { maximumFractionDigits: 2 });
  return `${v < 0 ? '-' : ''}R$ ${mantissa}${short ? m.short : m.suffix}`;
};

/** Money for a KPI tile: exact up to R$ 999.999,99, abbreviated from a million up ("R$ 1,25 mi",
 *  "R$ 3,4 bi"). The aside column is 360px wide, which leaves a tile ~130px for its value — an exact
 *  "R$ 1.234.567.890,00" has no font size that both fits there and stays readable, and letting it wrap
 *  breaks the number mid-digit. Pair it with the exact value in `title` (see `money()` in
 *  `budget-tiles.tsx`) so the precision is one hover away. */
export const brlKpi = (v: number | null | undefined) =>
  isBlank(v) ? NO_VALUE : Math.abs(v) < 1_000_000 ? brl(v) : abbreviate(v);

/** Money for a tight chart axis: abbreviates from a THOUSAND up, unlike `brlKpi` — a 10px axis label
 *  has far less room than a tile. Keeps the one-letter "K"/"M"/"B" forms for the same reason. */
export const brlCompact = (v: number | null | undefined) =>
  isBlank(v)
    ? NO_VALUE
    : Math.abs(v) >= 1_000_000
      ? abbreviate(v, true)
      : Math.abs(v) >= 1_000
        ? `${v < 0 ? '-' : ''}R$ ${(Math.abs(v) / 1_000).toLocaleString('pt-BR', { maximumFractionDigits: 0 })}K`
        : brl(v);

export function iniciais(nome: string) {
  // Só partes que COMEÇAM com letra: o nome do Google costuma trazer cargo/setor entre parênteses
  // ("Ana (Financeiro)"), e pegar o primeiro caractere cru dava "A(" no avatar.
  const p = nome.split(/\s+/).filter((w) => /^\p{L}/u.test(w));
  return ((p[0]?.[0] ?? '') + (p[1]?.[0] ?? p[0]?.[1] ?? '')).toUpperCase();
}
