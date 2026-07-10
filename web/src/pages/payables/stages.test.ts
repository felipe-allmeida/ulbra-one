import { describe, it, expect } from 'vitest';
import { STAGE_ORDER, STAGE_STATUS, dashboardCards, isStageKey } from './stages';

const summary = {
  entry: { count: 5, totalAmount: 12400 },
  classification: { count: 3, totalAmount: 8750 },
  approval: { count: 2, totalAmount: 45000 },
  accounting: { count: 1, totalAmount: 5000 },
  payment: { count: 4, totalAmount: 22100 },
  reconciled: { count: 18, totalAmount: 134500 },
  fiscalDocumentPending: { count: 1, totalAmount: 5000 },
};

describe('stages', () => {
  it('orders the six stages and maps each to its backend status', () => {
    expect(STAGE_ORDER).toEqual(['entry', 'classification', 'approval', 'accounting', 'payment', 'reconciled']);
    expect(STAGE_STATUS.entry).toBe('Received');
    expect(STAGE_STATUS.classification).toBe('PendingClassification');
    expect(STAGE_STATUS.approval).toBe('PendingApproval');
    expect(STAGE_STATUS.accounting).toBe('PendingAccounting');
    expect(STAGE_STATUS.payment).toBe('Scheduled');
    expect(STAGE_STATUS.reconciled).toBe('Paid');
  });
  it('builds one dashboard card per stage with translated label, count and amount', () => {
    const cards = dashboardCards(summary, (k) => k);
    expect(cards).toHaveLength(6);
    expect(cards[0]).toMatchObject({ stageKey: 'entry', label: 'payables.stages.entry', count: 5, totalAmount: 12400 });
    expect(cards[2]).toMatchObject({ stageKey: 'approval', count: 2, totalAmount: 45000 });
    expect(cards[3]).toMatchObject({ stageKey: 'accounting', count: 1, totalAmount: 5000 });
  });
  it('isStageKey guards valid keys', () => {
    expect(isStageKey('approval')).toBe(true);
    expect(isStageKey('bogus')).toBe(false);
    expect(isStageKey(null)).toBe(false);
  });
});
