import { describe, it, expect } from 'vitest';
import { remittanceCategoryLabel } from './badges';

// t() is passed in; use identity so we assert the exact key for the i18n-backed labels.
const t = (k: string) => k;

describe('remittanceCategoryLabel', () => {
  it('labels plain-key PIX and PIX EMV (copia e cola) distinctly — they are separate CNAB layouts', () => {
    expect(remittanceCategoryLabel('Pix', t)).toBe('PIX');
    expect(remittanceCategoryLabel('PixQrCode', t)).toBe('PIX EMV');
  });

  it('labels TED and boleto formas', () => {
    expect(remittanceCategoryLabel('Ted', t)).toBe('TED');
    expect(remittanceCategoryLabel('BoletoBancario', t)).toBe('remittances.category.boleto');
    expect(remittanceCategoryLabel('Convenio', t)).toBe('remittances.category.convenio');
  });

  it('falls back to a dash for null/undefined/unknown', () => {
    expect(remittanceCategoryLabel(null, t)).toBe('—');
    expect(remittanceCategoryLabel(undefined, t)).toBe('—');
    expect(remittanceCategoryLabel('Something', t)).toBe('—');
  });
});
