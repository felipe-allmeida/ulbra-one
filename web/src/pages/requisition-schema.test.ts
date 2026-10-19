import { describe, expect, it } from 'vitest';
import { requisitionSchema, type RequisitionValues } from './schemas';

const base: RequisitionValues = {
  unitId: 'unit_1',
  costCenterId: 'cc_1',
  vendorId: 'vendor_1',
  type: 'Purchase',
  description: 'Compra de material',
  observation: '',
  estimateValue: '1500',
  estimateType: 'Monthly',
  startCompetency: '2026-07',
  endCompetency: '',
  firstDueDate: '2026-07-10',
  paymentFrequency: '',
  paymentInstallmentsCount: '',
  projectId: null,
};

const issuesFor = (patch: Partial<RequisitionValues>) => {
  const r = requisitionSchema.safeParse({ ...base, ...patch });
  return r.success ? [] : r.error.issues;
};

describe('requisitionSchema', () => {
  it('accepts a valid purchase without a project', () => {
    expect(issuesFor({})).toHaveLength(0);
  });

  it('requires a project when the type is Project (AEL-792 ceiling link)', () => {
    const issues = issuesFor({ type: 'Project', projectId: null });
    expect(issues.some((i) => i.path[0] === 'projectId' && i.message === 'validation.projectRequired')).toBe(true);
  });

  it('accepts a Project requisition once a project is linked', () => {
    expect(issuesFor({ type: 'Project', projectId: 'proj_1' })).toHaveLength(0);
  });

  it('does not require a project for the other types', () => {
    for (const type of ['Purchase', 'ServiceProvision', 'FixedExpense'] as const) {
      expect(issuesFor({ type, projectId: null })).toHaveLength(0);
    }
  });

  it('accepts installments without a frequency (periodicidade dropped from the form)', () => {
    expect(issuesFor({ paymentFrequency: '', paymentInstallmentsCount: '3' })).toHaveLength(0);
  });

  it('rejects an installment count below 1', () => {
    const issues = issuesFor({ paymentInstallmentsCount: '0' });
    expect(
      issues.some((i) => i.path[0] === 'paymentInstallmentsCount' && i.message === 'validation.installmentsMin1'),
    ).toBe(true);
  });

  it('rejects an end competency before the start', () => {
    const issues = issuesFor({ startCompetency: '2026-07', endCompetency: '2026-06' });
    expect(issues.some((i) => i.message === 'validation.competencyEndBeforeStart')).toBe(true);
  });

  it('rejects a non-positive estimate value', () => {
    expect(issuesFor({ estimateValue: '0' }).some((i) => i.path[0] === 'estimateValue')).toBe(true);
  });
});
