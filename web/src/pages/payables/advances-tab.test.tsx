import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageItems, mockMe } = vi.hoisted(() => ({
  mockUseStageItems: vi.fn(),
  // Mutable identity; usePermissions() reads useMe(). Admin by default (non-gating tests see the action).
  mockMe: { isAdmin: true, permissions: [] as string[] },
}));
vi.mock('../../queries', () => ({ usePayableStageItems: mockUseStageItems, useMe: () => ({ data: mockMe }) }));
vi.mock('../../store', () => ({ useApp: () => ({ empresas: [] }) }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));
vi.mock('./accounting-drawer', () => ({
  AccountingDrawer: ({ payableId }: { payableId: string }) => <div data-testid='accounting-drawer'>{payableId}</div>,
}));

import { AdvancesTab } from './advances-tab';

const items = [
  {
    payableId: 'pay_1',
    counterpartyName: 'Fornecedor A',
    description: 'Adiantamento obra',
    costCenterId: 'cc_1',
    categoryId: null,
    firstDueDate: '2026-07-20',
    installmentCount: 1,
    totalAmount: 1000,
    totalWithheld: 0,
    netTotal: 1000,
    status: 'PendingAccounting',
    origin: 'Advance',
    hasFiscalDocument: false,
  },
  {
    payableId: 'pay_2',
    counterpartyName: 'Fornecedor B',
    description: 'Adiantamento viagem',
    costCenterId: 'cc_1',
    categoryId: null,
    firstDueDate: '2026-08-05',
    installmentCount: 1,
    totalAmount: 500,
    totalWithheld: 50,
    netTotal: 450,
    status: 'Paid',
    origin: 'Advance',
    hasFiscalDocument: false,
  },
];

beforeEach(() => {
  mockMe.isAdmin = true;
  mockMe.permissions = [];
  mockUseStageItems.mockReturnValue({ data: { items, total: 2, totalPages: 1 } });
});

function renderTab() {
  return render(
    <MemoryRouter>
      <AdvancesTab companyId='company_1' />
    </MemoryRouter>,
  );
}

describe('AdvancesTab (compact list)', () => {
  it('queries stage-items with origin=Advance and no status filter', () => {
    renderTab();
    expect(mockUseStageItems).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', origin: 'Advance' }),
      expect.anything(),
    );
    const call = mockUseStageItems.mock.calls[0][0];
    expect(call.status).toBeUndefined();
  });

  it('lists each advance with its natureza and actual status chip', () => {
    renderTab();
    expect(screen.getByText('Adiantamento obra')).toBeInTheDocument();
    expect(screen.getByText('Adiantamento viagem')).toBeInTheDocument();
    // The at-a-glance chip carries the per-row status (one awaiting accounting, one paid).
    expect(screen.getByText('Aguardando contabilidade')).toBeInTheDocument();
    expect(screen.getByText('Paga')).toBeInTheDocument();
  });

  it('shows "Registrar despesa" only on advances awaiting accounting', () => {
    renderTab();
    // Rows start collapsed. Expanding the awaiting-accounting advance → action present.
    fireEvent.click(screen.getByText('Adiantamento obra'));
    expect(screen.getAllByRole('button', { name: 'Registrar despesa' })).toHaveLength(1);
    // Expanding the paid advance exposes no extra action.
    fireEvent.click(screen.getByText('Adiantamento viagem'));
    expect(screen.getAllByRole('button', { name: 'Registrar despesa' })).toHaveLength(1);
  });

  it('opens the accounting drawer for the clicked advance', () => {
    renderTab();
    fireEvent.click(screen.getByText('Adiantamento obra')); // expand the row first
    fireEvent.click(screen.getByRole('button', { name: 'Registrar despesa' }));
    expect(screen.getByTestId('accounting-drawer')).toHaveTextContent('pay_1');
  });

  it('hides the "Registrar despesa" action without payables:write, keeping the list readable', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read'];
    renderTab();
    expect(screen.getByText('Adiantamento obra')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Adiantamento obra')); // expand → still no action
    expect(screen.queryByRole('button', { name: 'Registrar despesa' })).not.toBeInTheDocument();
  });
});
