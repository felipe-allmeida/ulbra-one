import { describe, it, expect } from 'vitest';
import { groupByPeriod } from './payment-periods';

const mk = (id: string, dueDate: string, amount: number) =>
  ({ payableId: id, installmentId: id, dueDate, amount }) as any;

describe('groupByPeriod', () => {
  // Thursday 2026-06-25
  const now = new Date(2026, 5, 25);
  it('buckets overdue and due-today into today, with subtotal', () => {
    const groups = groupByPeriod([mk('a', '2026-06-20', 100), mk('b', '2026-06-25', 50)], now);
    const today = groups.find((g) => g.key === 'today')!;
    expect(today.items).toHaveLength(2);
    expect(today.subtotal).toBe(150);
  });
  it('separates this week, next week and future', () => {
    const groups = groupByPeriod(
      [mk('c', '2026-06-27', 10), mk('d', '2026-06-30', 20), mk('e', '2026-07-20', 30)],
      now,
    );
    expect(groups.find((g) => g.key === 'thisWeek')?.items.map((i) => i.payableId)).toEqual(['c']);
    expect(groups.find((g) => g.key === 'nextWeek')?.items.map((i) => i.payableId)).toEqual(['d']);
    expect(groups.find((g) => g.key === 'future')?.items.map((i) => i.payableId)).toEqual(['e']);
  });
  it('omits empty groups and returns them in fixed order', () => {
    const groups = groupByPeriod([mk('f', '2026-07-20', 5)], now);
    expect(groups.map((g) => g.key)).toEqual(['future']);
  });
});
