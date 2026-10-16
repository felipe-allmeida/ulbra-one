import { describe, it, expect } from 'vitest';
import { groupByPeriod } from './receipt-periods';

const mk = (id: string, dueDate: string, amount: number) =>
  ({ receivableId: id, installmentId: id, dueDate, amount }) as unknown as import('../../api').ReceivableListItem;

describe('groupByPeriod (receivables)', () => {
  const now = new Date(2026, 5, 25); // Thursday
  it('buckets overdue and due-today into today with subtotal', () => {
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
    expect(groups.find((g) => g.key === 'thisWeek')?.items.map((i) => i.receivableId)).toEqual(['c']);
    expect(groups.find((g) => g.key === 'nextWeek')?.items.map((i) => i.receivableId)).toEqual(['d']);
    expect(groups.find((g) => g.key === 'future')?.items.map((i) => i.receivableId)).toEqual(['e']);
  });
});
