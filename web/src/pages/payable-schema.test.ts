import { describe, expect, it } from 'vitest';
import { previewInstallments, payableSchema } from './schemas';

const basePayable = {
  vendorId: 'vendor_1',
  costCenterId: 'cc_1',
  categoryId: null,
  accountingAccountId: null,
  financialAccountId: null,
  description: 'Compra',
  documentNumber: '',
  issueDate: '',
  mode: 'single' as const,
  totalAmount: '100',
  installmentCount: '2',
  firstDueDate: '2026-07-10',
  intervalMonths: '1',
};

const descriptionIssues = (description: string) => {
  const r = payableSchema.safeParse({ ...basePayable, description });
  return r.success ? [] : r.error.issues.filter((i) => i.path[0] === 'description');
};

describe('payableSchema description', () => {
  it('rejects a description longer than 300 chars (matches the DB varchar(300))', () => {
    const issues = descriptionIssues('a'.repeat(301));
    expect(issues.some((i) => i.message === 'validation.descriptionMax')).toBe(true);
  });

  it('accepts a description of exactly 300 chars', () => {
    expect(descriptionIssues('a'.repeat(300))).toHaveLength(0);
  });
});

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
