import { describe, expect, it } from 'vitest';
import { clampPct } from './projects';

describe('clampPct (largura das barras de acompanhamento do project)', () => {
  it('mantém percentuais dentro do trilho', () => {
    expect(clampPct(42.5)).toBe(42.5);
    expect(clampPct(0)).toBe(0);
    expect(clampPct(100)).toBe(100);
  });

  it('trava em 100 quando o teto é estourado', () => {
    // previsto 120% do teto: a barra não pode transbordar o trilho.
    expect(clampPct(120)).toBe(100);
  });

  it('trava em 0 para valores negativos', () => {
    expect(clampPct(-5)).toBe(0);
  });
});
