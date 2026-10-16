import { describe, it, expect } from 'vitest';
import { STAGE_ORDER, STAGE_STATUS, dashboardCards, isStageKey } from './stages';

const summary = {
  entry: { count: 5, totalAmount: 12400, netTotalAmount: null },
  classification: { count: 3, totalAmount: 8750, netTotalAmount: null },
  approval: { count: 2, totalAmount: 45000, netTotalAmount: null },
  accounting: { count: 1, totalAmount: 5000, netTotalAmount: null },
  taxAnalysis: { count: 2, totalAmount: 9800, netTotalAmount: null },
  payment: { count: 4, totalAmount: 22100, netTotalAmount: 20900 },
  reconciled: { count: 18, totalAmount: 134500, netTotalAmount: null },
  fiscalDocumentPending: { count: 1, totalAmount: 5000, netTotalAmount: null },
  fiscalDocumentPendingAccounting: { count: 1, totalAmount: 5000, netTotalAmount: null },
};

describe('stages', () => {
  it('orders the seven stages and maps each to its backend status', () => {
    expect(STAGE_ORDER).toEqual([
      'entry',
      'classification',
      'approval',
      'accounting',
      'taxAnalysis',
      'payment',
      'reconciled',
    ]);
    expect(STAGE_STATUS.entry).toBe('Received');
    expect(STAGE_STATUS.classification).toBe('PendingClassification');
    expect(STAGE_STATUS.approval).toBe('PendingApproval');
    expect(STAGE_STATUS.accounting).toBe('PendingAccounting');
    expect(STAGE_STATUS.taxAnalysis).toBe('PendingTaxAnalysis');
    expect(STAGE_STATUS.payment).toBe('Scheduled');
    expect(STAGE_STATUS.reconciled).toBe('Paid');
  });
  it('builds one dashboard card per stage with translated label, count and amount', () => {
    const cards = dashboardCards(summary, (k) => k);
    expect(cards).toHaveLength(7);
    expect(cards[0]).toMatchObject({ stageKey: 'entry', label: 'payables.stages.entry', count: 5, totalAmount: 12400 });
    expect(cards[2]).toMatchObject({ stageKey: 'approval', count: 2, totalAmount: 45000 });
    expect(cards[3]).toMatchObject({ stageKey: 'accounting', count: 1, totalAmount: 5000 });
    expect(cards[4]).toMatchObject({ stageKey: 'taxAnalysis', count: 2, totalAmount: 9800 });
  });
  it('payment card shows the NET sum (what actually leaves the bank); other stages stay gross', () => {
    const cards = dashboardCards(summary, (k) => k);
    expect(cards[5]).toMatchObject({ stageKey: 'payment', totalAmount: 20900 });
    expect(cards[6]).toMatchObject({ stageKey: 'reconciled', totalAmount: 134500 });
    // Defensive fallback while the API doesn't send the net field.
    const legacy = dashboardCards(
      { ...summary, payment: { count: 4, totalAmount: 22100, netTotalAmount: null } },
      (k) => k,
    );
    expect(legacy[5]).toMatchObject({ totalAmount: 22100 });
  });
  it('isStageKey guards valid keys', () => {
    expect(isStageKey('approval')).toBe(true);
    expect(isStageKey('bogus')).toBe(false);
    expect(isStageKey(null)).toBe(false);
  });
});
