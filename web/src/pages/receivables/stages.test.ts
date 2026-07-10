import { describe, it, expect } from 'vitest';
import { TAB_ORDER, TAB_STATUS, dashboardCards, cardTargetTab, isTabKey } from './stages';

const summary = {
  entry: { count: 4, totalAmount: 1200, netTotalAmount: null },
  receipt: { count: 2, totalAmount: 800, netTotalAmount: null },
  reconciled: { count: 9, totalAmount: 5000, netTotalAmount: null },
};

describe('receivable stages', () => {
  it('has two tabs mapped to receivable statuses', () => {
    expect(TAB_ORDER).toEqual(['areceber', 'reconciled']);
    expect(TAB_STATUS.areceber).toBe('AwaitingReceipt');
    expect(TAB_STATUS.reconciled).toBe('Received');
  });
  it('builds three dashboard cards from the summary buckets', () => {
    const cards = dashboardCards(summary, (k) => k);
    expect(cards.map((c) => c.stageKey)).toEqual(['entry', 'receipt', 'reconciled']);
    expect(cards[0]).toMatchObject({ count: 4, totalAmount: 1200, label: 'receivables.stages.entry' });
  });
  it('maps entry/receipt cards to the areceber tab and reconciled to reconciled', () => {
    expect(cardTargetTab('entry')).toBe('areceber');
    expect(cardTargetTab('receipt')).toBe('areceber');
    expect(cardTargetTab('reconciled')).toBe('reconciled');
  });
  it('isTabKey guards valid tab keys', () => {
    expect(isTabKey('areceber')).toBe(true);
    expect(isTabKey('entry')).toBe(false);
    expect(isTabKey(null)).toBe(false);
  });
});
