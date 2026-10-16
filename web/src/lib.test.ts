import { describe, it, expect } from 'vitest';
import { brl, brlCents, brlCompact, brlKpi, fmtPct, iniciais, parseMoneyInput, parsePercentInput } from './lib';

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
    for (const fn of [brl, brlCents, brlCompact, brlKpi, fmtPct]) {
      expect(fn(undefined)).toBe('—');
      expect(fn(null)).toBe('—');
      expect(fn(NaN)).toBe('—');
    }
  });

  // The 360px aside gives a KPI tile ~130px: an exact "R$ 1.234.567.890,00" cannot fit at a readable
  // size and used to wrap mid-number. Exact below a million, abbreviated above, sign before "R$"
  // (matching Intl's pt-BR currency), so a negative never reads "R$ -1,25 mi".
  describe('brlKpi — exact under a million, abbreviated above', () => {
    it('keeps values below a million exact', () => {
      expect(norm(brlKpi(42_500))).toBe('R$ 42.500,00');
      expect(norm(brlKpi(999_999.99))).toBe('R$ 999.999,99');
    });

    it('abbreviates millions, billions and trillions', () => {
      expect(norm(brlKpi(1_000_000))).toBe('R$ 1 mi');
      expect(norm(brlKpi(1_250_000))).toBe('R$ 1,25 mi');
      expect(norm(brlKpi(3_400_000_000))).toBe('R$ 3,4 bi');
      expect(norm(brlKpi(2_100_000_000_000))).toBe('R$ 2,1 tri');
    });

    it('puts the sign before the currency symbol', () => {
      expect(norm(brlKpi(-1_250_000))).toBe('-R$ 1,25 mi');
      expect(norm(brlKpi(-42_500))).toBe(norm(brl(-42_500)));
    });
  });

  // Regression: the magnitude checks used `v >= …`, so a negative never matched and a -R$ 2.000.000,00
  // came out unabbreviated onto a 10px chart axis; there was also no tier above millions.
  it('brlCompact abbreviates by magnitude, sign-safe, above millions too', () => {
    expect(norm(brlCompact(-2_500_000))).toBe('-R$ 2,5M');
    expect(norm(brlCompact(-42_000))).toBe('-R$ 42K');
    expect(norm(brlCompact(3_400_000_000))).toBe('R$ 3,4B');
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

describe('iniciais', () => {
  it('usa a inicial do primeiro e do segundo nome', () => {
    expect(iniciais('Felipe de Almeida')).toBe('FD');
  });

  it('cai para as duas primeiras letras de um nome único', () => {
    expect(iniciais('Maico')).toBe('MA');
  });

  // Um cargo/setor entre parênteses no nome do Google virava "A(" no avatar. Descartado o parêntese,
  // sobra só "Ana" — e o fallback de nome único ("AN") é o certo: o setor não é sobrenome.
  it('ignora partes do nome que não começam com letra', () => {
    expect(iniciais('Ana (Financeiro)')).toBe('AN');
    expect(iniciais('Ana - TI')).toBe('AT');
  });

  it('devolve vazio para um nome sem letra alguma', () => {
    expect(iniciais('   ')).toBe('');
    expect(iniciais('(...)')).toBe('');
  });
});
