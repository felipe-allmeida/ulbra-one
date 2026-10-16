import { describe, it, expect } from 'vitest';
import { brl, brlCents, brlCompact, fmtPct, parseMoneyInput, parsePercentInput } from './lib';

// Intl currency inserts a non-breaking space after "R$"; JS `\s` matches it (and the
// narrow no-break space), so normalize to a plain space for stable assertions.
const norm = (s: string) => s.replace(/\s/g, ' ');

// Regression: these formatters are used across many pages. A null/undefined/NaN value
// (a missing optional API field) used to throw `.toLocaleString of undefined`, which the
// ErrorBoundary caught → the "problema" screen on unrelated pages. They must degrade to a
// placeholder instead of crashing.
describe('currency/percent formatters — null-safety', () => {
  it('formats real numbers', () => {
    expect(norm(brl(1234))).toBe('R$ 1.234,00');
    expect(norm(brlCents(1234.5))).toBe('R$ 1.234,50');
    expect(norm(brlCompact(2_500_000))).toBe('R$ 2,5M');
    expect(norm(fmtPct(0.0065))).toBe('0,65%');
  });

  it('returns a placeholder (never throws) for null/undefined/NaN', () => {
    for (const fn of [brl, brlCents, brlCompact, fmtPct]) {
      expect(fn(undefined)).toBe('—');
      expect(fn(null)).toBe('—');
      expect(fn(NaN)).toBe('—');
    }
  });

  it('formats zero as a value, not a placeholder', () => {
    expect(norm(brl(0))).toBe('R$ 0,00');
    expect(norm(brlCents(0))).toBe('R$ 0,00');
  });
});

// Regression: withholding rates are typed by hand and Brazilian users enter decimals with a comma
// (INSS CPRB "3,5"). Number("3,5") is NaN, so the submit filter used to drop the entry and send
// withholdingRates empty — wiping the rate on update. parsePercentInput must accept commas.
describe('parsePercentInput — pt-BR decimals', () => {
  it('parses comma and dot decimals equally', () => {
    expect(parsePercentInput('3,5')).toBe(3.5);
    expect(parsePercentInput('3.5')).toBe(3.5);
    expect(parsePercentInput('4,65')).toBe(4.65);
    expect(parsePercentInput('11')).toBe(11);
    expect(parsePercentInput(' 1,5 ')).toBe(1.5);
  });

  it('returns null for blank or non-numeric input', () => {
    expect(parsePercentInput('')).toBeNull();
    expect(parsePercentInput('   ')).toBeNull();
    expect(parsePercentInput('abc')).toBeNull();
  });
});

// Regression: money fields are typed by hand in pt-BR ("1.234,56") but were parsed with a bare
// Number(), which yields NaN for grouped/comma amounts — the submit filter then dropped or zeroed
// the value. parseMoneyInput must treat ',' as the decimal and '.' as thousands grouping.
describe('parseMoneyInput — pt-BR money', () => {
  it('parses canonical, comma and dot amounts', () => {
    expect(parseMoneyInput('1.234,56')).toBe(1234.56);
    expect(parseMoneyInput('1234,5')).toBe(1234.5);
    expect(parseMoneyInput('1234.56')).toBe(1234.56);
    expect(parseMoneyInput('1234')).toBe(1234);
    expect(parseMoneyInput('1.000.000,00')).toBe(1000000);
    expect(parseMoneyInput(' 50,00 ')).toBe(50);
  });

  it('returns null for blank or non-numeric input', () => {
    expect(parseMoneyInput('')).toBeNull();
    expect(parseMoneyInput('   ')).toBeNull();
    expect(parseMoneyInput('abc')).toBeNull();
  });
});
