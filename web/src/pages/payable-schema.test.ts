import { describe, expect, it } from 'vitest';
import { previewInstallments } from './schemas';

describe('previewInstallments', () => {
  it('splits the total into installments, putting the rounding remainder on the last one', () => {
    const rows = previewInstallments(100, 3, '2026-07-10', 1);

    expect(rows.map((r) => r.amount)).toEqual([33.33, 33.33, 33.34]);
    expect(rows.reduce((sum, r) => sum + r.amount, 0)).toBeCloseTo(100, 2);
  });

  it('advances the due date by the interval each month', () => {
    const rows = previewInstallments(100, 3, '2026-07-10', 1);

    expect(rows.map((r) => r.dueDate)).toEqual(['2026-07-10', '2026-08-10', '2026-09-10']);
    expect(rows.map((r) => r.sequence)).toEqual([1, 2, 3]);
  });
});
