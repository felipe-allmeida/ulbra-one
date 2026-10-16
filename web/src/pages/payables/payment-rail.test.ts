import { describe, it, expect } from 'vitest';
import { railBucket } from './payment-rail';

const base = {
  installmentStatus: 'Open',
  isHeld: false,
  rail: 'Remittance',
  manualReason: null,
  scheduledPaymentDate: '2026-07-15',
  remittanceSequence: null,
} as const;
const today = '2026-07-10';

describe('railBucket', () => {
  it('manual rail → manual', () =>
    expect(railBucket({ ...base, rail: 'Manual', manualReason: 'VendorBankInfoMissing' }, today)).toBe('manual'));
  it('held → held', () => expect(railBucket({ ...base, isHeld: true }, today)).toBe('held'));
  it('open + scheduled future → scheduled', () => expect(railBucket(base, today)).toBe('scheduled'));
  it('open + scheduled today/past → nextSweep', () =>
    expect(railBucket({ ...base, scheduledPaymentDate: '2026-07-10' }, today)).toBe('nextSweep'));
  it('in remittance → inRemittance', () =>
    expect(railBucket({ ...base, installmentStatus: 'InRemittance', remittanceSequence: 7 }, today)).toBe(
      'inRemittance',
    ));
  it('paid → paid', () => expect(railBucket({ ...base, installmentStatus: 'Paid' }, today)).toBe('paid'));
});
