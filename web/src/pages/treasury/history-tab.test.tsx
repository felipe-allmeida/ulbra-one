import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockHistory } = vi.hoisted(() => ({ mockHistory: vi.fn() }));
vi.mock('../../queries', () => ({
  usePaymentsHistory: mockHistory,
  useBankAccounts: () => ({ data: [{ id: 'ba_1', alias: 'Banrisul c/c 41.221' }] }),
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
}));

import { PaymentsHistoryTab } from './history-tab';
import { SYSTEM_ACTOR_ID } from '../../lib';

const basePayment = {
  paymentId: 'pmt_1',
  payableId: 'pay_1',
  installmentId: 'ins_1',
  installmentSequence: 1,
  installmentCount: 2,
  counterpartyName: 'Fornecedor Alfa',
  vendorDocument: '92802784000190',
  documentNumber: 'NF-10',
  paymentDate: '2026-08-01',
  amount: 1234.56,
  interest: 0,
  fine: 0,
  discount: 0,
  method: 'Pix',
  bankAccountId: 'ba_1',
  isReversed: false,
  reversalReason: null,
  hasProof: false,
  createdAt: '2026-08-01T12:00:00Z',
  createdById: 'user-1',
};

const renderTab = () =>
  render(
    <MemoryRouter>
      <PaymentsHistoryTab companyId='c1' search='' />
    </MemoryRouter>,
  );

beforeEach(() => {
  mockHistory.mockReturnValue({
    data: {
      items: [
        { ...basePayment },
        {
          ...basePayment,
          paymentId: 'pmt_2',
          counterpartyName: 'Fornecedor Beta',
          paymentDate: '2026-07-15',
          isReversed: true,
          reversalReason: 'erro',
          hasProof: true,
          createdById: SYSTEM_ACTOR_ID,
        },
      ],
      total: 2,
      totalPages: 1,
    },
  });
});

describe('PaymentsHistoryTab', () => {
  it('groups payments by month with pt-BR labels', () => {
    renderTab();
    expect(screen.getByText('Agosto de 2026')).toBeInTheDocument();
    expect(screen.getByText('Julho de 2026')).toBeInTheDocument();
    expect(screen.getByText('Fornecedor Alfa')).toBeInTheDocument();
    expect(screen.getByText('Fornecedor Beta')).toBeInTheDocument();
  });

  it('shows amount with cents, bank alias, parcela and document meta', () => {
    renderTab();
    expect(screen.getAllByText('R$ 1.234,56').length).toBe(2);
    expect(screen.getAllByText(/Banrisul c\/c 41\.221/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/parcela 1\/2/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/#NF-10/).length).toBeGreaterThan(0);
  });

  it('flags reversed and automatic baixas and offers the proof download', () => {
    renderTab();
    expect(screen.getByText('Estornado')).toBeInTheDocument();
    expect(screen.getByText('Automática')).toBeInTheDocument();
    expect(screen.getByText('Comprovante')).toBeInTheDocument();
  });

  it('shows the empty message when there are no payments', () => {
    mockHistory.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
    renderTab();
    expect(screen.getByText('Nenhum pagamento no período.')).toBeInTheDocument();
  });
});
