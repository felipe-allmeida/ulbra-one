import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';

const { mockRegister } = vi.hoisted(() => ({ mockRegister: vi.fn() }));
vi.mock('../../queries', () => ({
  useBankAccounts: () => ({ data: [] }),
  useRegisterPayment: () => ({ mutate: mockRegister, isPending: false, error: null }),
  useUploadPaymentProof: () => ({ mutate: vi.fn(), isPending: false, error: null }),
}));

import { PaymentForm } from './payment-form';

const payable = { id: 'payable_1', companyId: 'company_1' } as never;

function renderForm(installment: { amount: number; netAmount: number; paidAmount: number }) {
  return render(
    <PaymentForm
      payable={payable}
      installment={{ id: 'i1', sequence: 1, dueDate: '2026-07-10', status: 'Open', payments: [], ...installment } as never}
      onDone={() => {}}
      onCancel={() => {}}
    />,
  );
}

function amountInput(): HTMLInputElement {
  // The amount field is the first numeric input after the date ("Valor pago (R$)").
  const label = screen.getByText('Valor pago (R$)');
  return label.closest('label')!.parentElement!.querySelector('input')!;
}

describe('PaymentForm', () => {
  beforeEach(() => vi.clearAllMocks());

  it('suggests the NET outstanding balance (gross would be rejected with Payment.Overpay)', () => {
    renderForm({ amount: 1000, netAmount: 850, paidAmount: 0 });
    expect(amountInput().value).toBe('850');
  });

  it('suggests net minus what was already paid on partial payments', () => {
    renderForm({ amount: 1000, netAmount: 850, paidAmount: 500 });
    expect(amountInput().value).toBe('350');
  });

  it('without withholdings the net equals the gross', () => {
    renderForm({ amount: 1000, netAmount: 1000, paidAmount: 0 });
    expect(amountInput().value).toBe('1000');
  });
});
