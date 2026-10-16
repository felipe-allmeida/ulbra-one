import { describe, it, expect } from 'vitest';
import { businessHoursElapsed, stageSla } from './sla';

// Datas locais. 2026-01-01 é quinta → 2026-01-05 é segunda, 2026-01-09 sexta, 10 sáb, 11 dom, 12 seg.
describe('businessHoursElapsed', () => {
  it('conta horas dentro de um dia útil', () => {
    const start = new Date(2026, 0, 5, 0, 0);
    const now = new Date(2026, 0, 5, 5, 0);
    expect(businessHoursElapsed(start, now)).toBe(5);
  });

  it('pula fim de semana', () => {
    const start = new Date(2026, 0, 9, 22, 0); // sexta 22h
    const now = new Date(2026, 0, 12, 2, 0); // segunda 02h
    // sex 22,23 (2) + sáb/dom (0) + seg 00,01 (2) = 4
    expect(businessHoursElapsed(start, now)).toBe(4);
  });

  it('exclui feriado fixo (Natal 25/12)', () => {
    expect(businessHoursElapsed(new Date(2026, 11, 25, 0, 0), new Date(2026, 11, 26, 0, 0))).toBe(0);
  });

  it('exclui feriado móvel (Sexta-feira Santa 03/04/2026, Páscoa 05/04)', () => {
    expect(businessHoursElapsed(new Date(2026, 3, 3, 0, 0), new Date(2026, 3, 4, 0, 0))).toBe(0);
  });

  it('exclui feriado fixo Consciência Negra (20/11, nacional desde 2024)', () => {
    // 20/11/2026 é sexta-feira → nenhum minuto útil no dia.
    expect(businessHoursElapsed(new Date(2026, 10, 20, 0, 0), new Date(2026, 10, 21, 0, 0))).toBe(0);
  });
});

describe('stageSla', () => {
  it('dentro do prazo → tone ok, não atrasado', () => {
    const s = stageSla(new Date(2026, 0, 5, 0, 0).toISOString(), 24, new Date(2026, 0, 5, 5, 0));
    expect(s).not.toBeNull();
    expect(s!.overdue).toBe(false);
    expect(s!.remaining).toBe(19);
    expect(s!.tone).toBe('ok');
  });

  it('excedido → overdue + danger', () => {
    // seg 00h → qui 00h = 3 dias úteis (72h úteis) > 24
    const s = stageSla(new Date(2026, 0, 5, 0, 0).toISOString(), 24, new Date(2026, 0, 8, 0, 0));
    expect(s!.overdue).toBe(true);
    expect(s!.tone).toBe('danger');
  });

  it('data inválida → null', () => {
    expect(stageSla('não-é-data')).toBeNull();
  });
});
