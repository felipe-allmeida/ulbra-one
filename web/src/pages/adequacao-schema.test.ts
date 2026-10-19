import { describe, expect, it } from 'vitest';
import { budgetAdjustmentSchema, type BudgetAdjustmentTypeValue } from './schemas';

const base: {
  unitId: string;
  costCenterId: string;
  type: BudgetAdjustmentTypeValue;
  competency: string;
  sourceCompetency: string;
  value: string;
  justification: string;
  counterparts: string;
} = {
  unitId: 'unit_1',
  costCenterId: 'cc_1',
  type: 'Supplementation',
  competency: '2026-07',
  sourceCompetency: '',
  value: '500',
  justification: 'Motivo do ajuste',
  counterparts: 'Entregas',
};

const issuesFor = (patch: Partial<typeof base>) => {
  const r = budgetAdjustmentSchema.safeParse({ ...base, ...patch });
  return r.success ? [] : r.error.issues;
};

describe('budgetAdjustmentSchema', () => {
  it('accepts a valid supplementation', () => {
    expect(issuesFor({})).toHaveLength(0);
  });

  it('requires counterparts except for a reduction', () => {
    const supp = issuesFor({ type: 'Supplementation', counterparts: '' });
    expect(supp.some((i) => i.path[0] === 'counterparts' && i.message === 'validation.counterpartsRequired')).toBe(
      true,
    );

    // Reduction without counterparts is fine.
    expect(issuesFor({ type: 'Reduction', counterparts: '' })).toHaveLength(0);
  });

  it('requires a source competency for a transfer', () => {
    const issues = issuesFor({ type: 'Transfer', sourceCompetency: '', competency: '2026-08' });
    expect(
      issues.some((i) => i.path[0] === 'sourceCompetency' && i.message === 'validation.sourceCompetencyRequired'),
    ).toBe(true);
  });

  it('rejects a transfer whose source equals the destination', () => {
    const issues = issuesFor({ type: 'Transfer', sourceCompetency: '2026-07', competency: '2026-07' });
    expect(issues.some((i) => i.message === 'validation.sourceEqualsDestination')).toBe(true);
  });

  it('rejects a source competency on a non-transfer', () => {
    const issues = issuesFor({ type: 'Supplementation', sourceCompetency: '2026-06' });
    expect(issues.some((i) => i.message === 'validation.sourceNotAllowed')).toBe(true);
  });

  it('accepts a valid transfer between distinct competencies', () => {
    expect(issuesFor({ type: 'Transfer', sourceCompetency: '2026-06', competency: '2026-07' })).toHaveLength(0);
  });

  it('rejects a non-positive value', () => {
    expect(issuesFor({ value: '0' }).some((i) => i.path[0] === 'value')).toBe(true);
  });
});
