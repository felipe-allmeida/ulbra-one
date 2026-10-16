import { describe, expect, it } from 'vitest';
import { paymentSchema } from './schemas';

describe('paymentSchema', () => {
  const validPayload = {
    paymentDate: '2026-07-10',
    amount: '150.00',
    bankAccountId: 'bank_account_1',
    method: 'Pix' as const,
    interest: '0',
    fine: '0',
    discount: '0',
    notes: '',
  };

  it('passes for a valid payment payload', () => {
    const result = paymentSchema.safeParse(validPayload);
    expect(result.success).toBe(true);
  });

  it('fails when amount is zero', () => {
    const result = paymentSchema.safeParse({ ...validPayload, amount: '0' });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.some((i) => i.path[0] === 'amount')).toBe(true);
    }
  });
});
