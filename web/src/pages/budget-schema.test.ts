import { describe, expect, it } from 'vitest';
import { unitBudgetSchema } from './schemas';

const base = {
  unitId: 'unit_1',
  startCompetency: '2026-07',
  endCompetency: '',
  monthlyLimit: '50000',
  active: true,
};

const issuesFor = (patch: Partial<typeof base>) => {
  const r = unitBudgetSchema.safeParse({ ...base, ...patch });
  return r.success ? [] : r.error.issues;
};

describe('unitBudgetSchema', () => {
  it('accepts a start with an empty end (backend applies the 12-month default)', () => {
    expect(issuesFor({})).toHaveLength(0);
  });

  it('accepts a period with a valid end', () => {
    expect(issuesFor({ endCompetency: '2027-06' })).toHaveLength(0);
  });

  it('accepts a single-month period (end == start)', () => {
    expect(issuesFor({ endCompetency: '2026-07' })).toHaveLength(0);
  });

  it('rejects an end before the start', () => {
    const issues = issuesFor({ startCompetency: '2026-07', endCompetency: '2026-06' });
    expect(
      issues.some((i) => i.path[0] === 'endCompetency' && i.message === 'validation.competencyEndBeforeStart'),
    ).toBe(true);
  });

  it('rejects a period longer than 36 months', () => {
    const issues = issuesFor({ startCompetency: '2026-01', endCompetency: '2029-02' }); // 38 months
    expect(issues.some((i) => i.path[0] === 'endCompetency' && i.message === 'validation.competencyHorizonMax')).toBe(
      true,
    );
  });

  it('accepts exactly 36 months', () => {
    expect(issuesFor({ startCompetency: '2026-01', endCompetency: '2028-12' })).toHaveLength(0);
  });

  it('rejects a negative limit', () => {
    expect(issuesFor({ monthlyLimit: '-1' }).some((i) => i.path[0] === 'monthlyLimit')).toBe(true);
  });
});
