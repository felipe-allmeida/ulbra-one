import { describe, it, expect } from 'vitest';
import {
  competencyLabel,
  formatVendorDocument,
  groupQueueByDueDate,
  isAgreementBoleto,
  queueRowNumber,
  summaryWindowNet,
  truncateDigitableLine,
} from './queue-groups';
import { type PayableListItem, type PaymentQueueSummary } from '../../api';

const item = (over: Partial<PayableListItem>): PayableListItem => ({
  payableId: 'pay_00000000000000000000000000000abc',
  installmentId: 'ins_1',
  sequence: 1,
  installmentCount: 1,
  counterpartyName: 'Fornecedor',
  description: '',
  costCenterId: 'cc_1',
  categoryId: null,
  dueDate: '2026-08-04',
  amount: 100,
  netAmount: 100,
  installmentStatus: 'Open',
  payableStatus: 'Scheduled',
  scheduledPaymentDate: '2026-08-04',
  isHeld: false,
  holdReason: null,
  remittanceId: null,
  remittanceSequence: null,
  rail: 'Remittance',
  manualReason: null,
  vendorDocument: null,
  vendorId: 'ven_1',
  effectiveMethod: null,
  boletoDigitableLine: null,
  documentNumber: null,
  issueDate: null,
  originUnitId: null,
  hasFiscalDocument: true,
  ...over,
});

describe('groupQueueByDueDate', () => {
  it('splits overdue into a single leading bucket and the rest per day ascending', () => {
    const today = '2026-08-04';
    const groups = groupQueueByDueDate(
      [
        item({ installmentId: 'i1', dueDate: '2026-08-05' }),
        item({ installmentId: 'i2', dueDate: '2026-08-01' }),
        item({ installmentId: 'i3', dueDate: '2026-08-04' }),
        item({ installmentId: 'i4', dueDate: '2026-07-20' }),
        item({ installmentId: 'i5', dueDate: '2026-08-05' }),
      ],
      today,
    );
    expect(groups.map((g) => g.key)).toEqual(['overdue', '2026-08-04', '2026-08-05']);
    expect(groups[0].items.map((i) => i.installmentId)).toEqual(['i2', 'i4']);
    expect(groups[2].items).toHaveLength(2);
  });

  it('omits the overdue bucket when nothing is overdue', () => {
    const groups = groupQueueByDueDate([item({ dueDate: '2026-08-04' })], '2026-08-04');
    expect(groups.map((g) => g.key)).toEqual(['2026-08-04']);
  });
});

describe('isAgreementBoleto', () => {
  it('true for a 48-digit line starting with 8 (convênio)', () => {
    expect(isAgreementBoleto('8' + '1'.repeat(47))).toBe(true);
  });
  it('false for a 47-digit bank boleto or empty', () => {
    expect(isAgreementBoleto('0' + '1'.repeat(46))).toBe(false);
    expect(isAgreementBoleto(null)).toBe(false);
    expect(isAgreementBoleto('')).toBe(false);
  });
});

describe('formatVendorDocument', () => {
  it('formats CNPJ and CPF', () => {
    expect(formatVendorDocument('92802784000190')).toBe('92.802.784/0001-90');
    expect(formatVendorDocument('12345678909')).toBe('123.456.789-09');
  });
  it('passes through unrecognized values', () => {
    expect(formatVendorDocument('123')).toBe('123');
    expect(formatVendorDocument(null)).toBe('');
  });
});

describe('row labels', () => {
  it('competencyLabel = MM/yyyy of issue date', () => {
    expect(competencyLabel('2026-08-15')).toBe('08/2026');
    expect(competencyLabel(null)).toBe('—');
  });
  it('queueRowNumber prefers the document number, else short id', () => {
    expect(queueRowNumber({ documentNumber: '31854541', payableId: 'pay_x' })).toBe('#31854541');
    expect(queueRowNumber({ documentNumber: null, payableId: 'pay_00000000000000000000000000000abc' })).toBe('#000ABC');
  });
  it('truncateDigitableLine keeps start and end', () => {
    const line = '84660000000549040101045302662026080421355277';
    const out = truncateDigitableLine(line);
    expect(out).toContain('…');
    expect(out.startsWith('846600000005')).toBe(true);
    expect(out.endsWith('21355277')).toBe(true);
  });
});

describe('summaryWindowNet', () => {
  const summary: PaymentQueueSummary = {
    asOf: '2026-08-04',
    overdueCount: 2,
    overdueNetTotal: 500,
    groups: [
      { dueDate: '2026-08-04', count: 1, netTotal: 100 },
      { dueDate: '2026-08-10', count: 1, netTotal: 200 },
      { dueDate: '2026-08-20', count: 1, netTotal: 400 },
    ],
    totalCount: 5,
    totalNetTotal: 1200,
  };
  it('sums only buckets inside the window', () => {
    expect(summaryWindowNet(summary, '2026-08-04', '2026-08-18')).toBe(300);
    expect(summaryWindowNet(summary, '2026-08-05', '2026-08-31')).toBe(600);
  });
});
