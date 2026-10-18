import { describe, it, expect } from 'vitest';
import {
  competencyWindowText,
  fmtCompetency,
  monthsBetween,
  shiftCompetency,
  spansCompetencyWindow,
} from './competency';

const REC = 'recorrente';

describe('spansCompetencyWindow', () => {
  it('Conta fixa abrange independentemente da estimativa', () => {
    expect(spansCompetencyWindow({ type: 'FixedExpense', estimateType: 'Full' })).toBe(true);
  });

  it('estimativa Mensal abrange em qualquer tipo', () => {
    expect(spansCompetencyWindow({ type: 'Purchase', estimateType: 'Monthly' })).toBe(true);
    expect(spansCompetencyWindow({ type: 'Project', estimateType: 'Monthly' })).toBe(true);
  });

  it('Integral (Full) em tipo não-recorrente = mês único', () => {
    expect(spansCompetencyWindow({ type: 'Purchase', estimateType: 'Full' })).toBe(false);
    expect(spansCompetencyWindow({ type: 'Project', estimateType: 'Full' })).toBe(false);
    expect(spansCompetencyWindow({ type: 'ServiceProvision', estimateType: 'Full' })).toBe(false);
  });
});

describe('competencyWindowText', () => {
  it('mês único (Full não recorrente): só a competência inicial', () => {
    expect(
      competencyWindowText(
        { type: 'Purchase', estimateType: 'Full', startCompetency: '2026-07', endCompetency: null },
        REC,
      ),
    ).toBe('07/2026');
  });

  it('recorrência aberta (sem fim): "início → recorrente"', () => {
    expect(
      competencyWindowText(
        { type: 'FixedExpense', estimateType: 'Monthly', startCompetency: '2026-07', endCompetency: null },
        REC,
      ),
    ).toBe('07/2026 → recorrente');
  });

  it('Mensal sem fim também é aberta, mesmo em tipo Compra', () => {
    expect(
      competencyWindowText(
        { type: 'Purchase', estimateType: 'Monthly', startCompetency: '2026-07', endCompetency: null },
        REC,
      ),
    ).toBe('07/2026 → recorrente');
  });

  it('janela fechada (com fim): "início–fim"', () => {
    expect(
      competencyWindowText(
        { type: 'FixedExpense', estimateType: 'Monthly', startCompetency: '2026-07', endCompetency: '2026-12' },
        REC,
      ),
    ).toBe('07/2026–12/2026');
  });

  it('endCompetency é ignorada quando o tipo não abrange (defensivo, alinha com o guard)', () => {
    expect(
      competencyWindowText(
        { type: 'Purchase', estimateType: 'Full', startCompetency: '2026-07', endCompetency: '2026-12' },
        REC,
      ),
    ).toBe('07/2026');
  });
});

describe('fmtCompetency', () => {
  it('formata "YYYY-MM" como "MM/YYYY" e trata ausência', () => {
    expect(fmtCompetency('2026-07')).toBe('07/2026');
    expect(fmtCompetency(null)).toBe('—');
    expect(fmtCompetency(undefined)).toBe('—');
  });
});

describe('shiftCompetency', () => {
  it('desloca meses com virada de ano', () => {
    expect(shiftCompetency('2026-07', 11)).toBe('2027-06');
    expect(shiftCompetency('2026-12', 1)).toBe('2027-01');
    expect(shiftCompetency('2026-01', -1)).toBe('2025-12');
  });
  it('ecoa entrada inválida', () => {
    expect(shiftCompetency('', 11)).toBe('');
  });
});

describe('monthsBetween', () => {
  it('conta inclusivo', () => {
    expect(monthsBetween('2026-07', '2026-07')).toBe(1);
    expect(monthsBetween('2026-07', '2027-06')).toBe(12);
    expect(monthsBetween('2026-11', '2027-02')).toBe(4);
  });
  it('retorna 0 para inválido ou end < start', () => {
    expect(monthsBetween('2026-07', '2026-06')).toBe(0);
    expect(monthsBetween('', '2026-07')).toBe(0);
  });
});
