import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUsePayableStageItems, mockApprove, mockReject, mockUseCashFlow, mockUseMe } = vi.hoisted(() => ({
  mockUsePayableStageItems: vi.fn(),
  mockApprove: vi.fn(),
  mockReject: vi.fn(),
  mockUseCashFlow: vi.fn(),
  mockUseMe: vi.fn(),
}));
vi.mock('../../queries', () => ({
  usePayableStageItems: mockUsePayableStageItems,
  useApprovePayable: () => ({ mutate: mockApprove, isPending: false }),
  useRejectPayable: () => ({ mutate: mockReject, isPending: false }),
  useCashFlow: mockUseCashFlow,
  useMe: mockUseMe,
}));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));

import { ApprovalTab } from './approval-tab';

const item = {
  payableId: 'payable_1',
  counterpartyName: 'ACME',
  description: 'NF 1',
  costCenterId: 'cc_1',
  categoryId: null,
  firstDueDate: '2026-06-30',
  installmentCount: 1,
  totalAmount: 1000,
  status: 'PendingApproval',
};

beforeEach(() => {
  mockUseMe.mockReturnValue({ data: { isAdmin: true, permissions: [] } });
  mockUsePayableStageItems.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } });
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
    expect(mockUsePayableStageItems).toHaveBeenCalledWith(
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
  it('hides approve/reject actions without payables:approve', () => {
    // Cargo sem a permissão de aprovação (ex.: Financeiro) — vê a fila, mas não as ações.
    mockUseMe.mockReturnValue({ data: { isAdmin: false, permissions: ['payables:read'] } });
    render(
      <MemoryRouter>
        <ApprovalTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(screen.getByText('NF 1')).toBeInTheDocument();
    expect(screen.queryByText('Aprovar')).not.toBeInTheDocument();
    expect(screen.queryByText('Rejeitar')).not.toBeInTheDocument();
  });
});
