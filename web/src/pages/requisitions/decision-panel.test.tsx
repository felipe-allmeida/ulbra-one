import { describe, expect, it } from 'vitest';
import { type BudgetMonth } from '../../api';
import { tightestMonth } from './decision-panel';
import { transitionsFor } from './transitions';

const month = (over: Partial<BudgetMonth>): BudgetMonth => ({
  competency: '2026-08',
  orcado: 10000,
  realized: 0,
  committed: 0,
  forecast: 0,
  consumed: 0,
  available: 10000,
  budgeted: true,
  cost: 0,
  ...over,
});

describe('tightestMonth', () => {
  it('sem meses não há número a mostrar', () => {
    expect(tightestMonth([])).toBeNull();
  });

  // Uma requisição recorrente cobre várias competências: mostrar a primeira faria o painel prometer
  // folga que o mês seguinte não tem.
  it('escolhe a competência com a menor sobra depois do custo, não a primeira', () => {
    const folgada = month({ competency: '2026-08', available: 9000, cost: 1000 });
    const apertada = month({ competency: '2026-09', available: 2000, cost: 1800 });
    expect(tightestMonth([folgada, apertada])?.competency).toBe('2026-09');
  });

  it('a sobra negativa vence a positiva', () => {
    const estoura = month({ competency: '2026-10', available: 500, cost: 900 });
    expect(tightestMonth([month({ available: 8000, cost: 100 }), estoura])?.competency).toBe('2026-10');
  });
});

describe('transitionsFor', () => {
  const none = { canWrite: false, canTriage: false, canApprove: false };

  // O aside só existe se houver o que colocar nele: sem nenhuma ação a coluna de 360px abriria vazia.
  it('sem permissão nenhuma não há ação', () => {
    expect(transitionsFor('Approval', none).any).toBe(false);
  });

  it('status terminal não oferece nem cancelar', () => {
    const all = { canWrite: true, canTriage: true, canApprove: true };
    expect(transitionsFor('Paid', all).any).toBe(false);
    expect(transitionsFor('Cancelled', all).any).toBe(false);
    expect(transitionsFor('Finalized', all).any).toBe(false);
    expect(transitionsFor('AwaitingFiscalDocument', all).any).toBe(false);
  });

  it('rascunho do solicitante: enviar, editar, cancelar e excluir', () => {
    const f = transitionsFor('Draft', { ...none, canWrite: true });
    expect(f).toMatchObject({ showSubmit: true, showEdit: true, showDelete: true, showCancel: true, any: true });
    expect(f.showApprove).toBe(false);
  });

  it('aprovador em Aprovação: aprovar, solicitar ajuste e recusar — sem excluir', () => {
    const f = transitionsFor('Approval', { ...none, canApprove: true });
    expect(f).toMatchObject({ showApprove: true, showReturn: true, showCancel: true, any: true });
    expect(f.showDelete).toBe(false);
  });

  // Um Complemento sem outras ações precisa manter o Reenviar — foi o caso que quase sumiu ao mover o painel.
  it('complemento do solicitante mantém o reenviar', () => {
    expect(transitionsFor('Complement', { ...none, canWrite: true }).showResubmit).toBe(true);
  });
});
