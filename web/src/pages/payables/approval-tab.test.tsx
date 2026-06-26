import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUsePayables, mockApprove, mockReject, mockUseCashFlow } = vi.hoisted(() => ({
  mockUsePayables: vi.fn(),
  mockApprove: vi.fn(),
  mockReject: vi.fn(),
  mockUseCashFlow: vi.fn(),
}));
vi.mock('../../queries', () => ({
  usePayables: mockUsePayables,
  useApprovePayable: () => ({ mutate: mockApprove, isPending: false }),
  useRejectPayable: () => ({ mutate: mockReject, isPending: false }),
  useCashFlow: mockUseCashFlow,
}));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));

import { ApprovalTab } from './approval-tab';

const item = {
  payableId: 'payable_1',
  installmentId: 'i1',
  sequence: 1,
  installmentCount: 1,
  counterpartyName: 'ACME',
  description: 'NF 1',
  costCenterId: 'cc_1',
  categoryId: null,
  dueDate: '2026-06-30',
  amount: 1000,
  installmentStatus: 'Open',
  payableStatus: 'PendingApproval',
};

beforeEach(() => {
  mockUsePayables.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } });
  mockUseCashFlow.mockReturnValue({
    data: {
      startingBalance: 5000,
      kpis: { lowestProjectedBalance: 4000 },
      periods: [{ closingBalance: 4000, belowReserve: false }],
    },
    isLoading: false,
  });
});

describe('ApprovalTab', () => {
  it('lists pending-approval payables with an approve action', () => {
    render(
      <MemoryRouter>
        <ApprovalTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(screen.getByText('NF 1')).toBeInTheDocument();
    expect(mockUsePayables).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'PendingApproval' }),
      expect.anything(),
    );
  });
  it('rejects with a reason', () => {
    render(
      <MemoryRouter>
        <ApprovalTab companyId='company_1' />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Rejeitar'));
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'duplicado' } });
    fireEvent.click(screen.getByText('Confirmar'));
    expect(mockReject).toHaveBeenCalledWith({ id: 'payable_1', reason: 'duplicado' }, expect.any(Object));
  });
});
