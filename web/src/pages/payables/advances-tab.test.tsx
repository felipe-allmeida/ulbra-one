import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageItems, mockMe } = vi.hoisted(() => ({
  mockUseStageItems: vi.fn(),
  // Mutable identity; usePermissions() reads useMe(). Admin by default (non-gating tests see the action).
  mockMe: { isAdmin: true, permissions: [] as string[] },
}));
vi.mock('../../queries', () => ({ usePayableStageItems: mockUseStageItems, useMe: () => ({ data: mockMe }) }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));
vi.mock('./accounting-drawer', () => ({
  AccountingDrawer: ({ payableId }: { payableId: string }) => (
    <div data-testid='accounting-drawer'>{payableId}</div>
  ),
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

describe('AdvancesTab', () => {
  it('queries stage-items with origin=Advance and no status filter', () => {
    renderTab();
    expect(mockUseStageItems).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', origin: 'Advance' }),
      expect.anything(),
    );
    const call = mockUseStageItems.mock.calls[0][0];
    expect(call.status).toBeUndefined();
  });

  it('shows each advance with its actual status badge and the Adiantamento tag', () => {
    renderTab();
    expect(screen.getByText('Adiantamento obra')).toBeInTheDocument();
    expect(screen.getByText('Adiantamento viagem')).toBeInTheDocument();
    // Actual per-row status (not a fixed stage label): one pending accounting, one paid.
    expect(screen.getByText('Aguardando contabilidade')).toBeInTheDocument();
    expect(screen.getByText('Paga')).toBeInTheDocument();
    // Every row is tagged as an Adiantamento.
    expect(screen.getAllByText('Adiantamento')).toHaveLength(2);
  });

  it('shows a "Registrar despesa" action only on advances awaiting accounting', () => {
    renderTab();
    // pay_1 is PendingAccounting → actionable; pay_2 is Paid → no action.
    const pendingRow = screen.getByText('Adiantamento obra').closest('tr')!;
    const paidRow = screen.getByText('Adiantamento viagem').closest('tr')!;
    expect(within(pendingRow).getByRole('button', { name: 'Registrar despesa' })).toBeInTheDocument();
    expect(within(paidRow).queryByRole('button', { name: 'Registrar despesa' })).not.toBeInTheDocument();
  });

  it('opens the accounting drawer for the clicked advance', () => {
    renderTab();
    const pendingRow = screen.getByText('Adiantamento obra').closest('tr')!;
    fireEvent.click(within(pendingRow).getByRole('button', { name: 'Registrar despesa' }));
    expect(screen.getByTestId('accounting-drawer')).toHaveTextContent('pay_1');
  });

  it('shows gross + net columns: net filled once locked (Paid), "—" while pre-accounting', () => {
    renderTab();
    expect(screen.getByText('Valor bruto')).toBeInTheDocument();
    expect(screen.getByText('Valor líquido')).toBeInTheDocument();
    // Paid advance → net cell shows 450; PendingAccounting → net cell shows '—'
    // (each row also has one '—' in the unmapped cost-center cell).
    const paidRow = screen.getByText('Adiantamento viagem').closest('tr')!;
    expect(within(paidRow).getByText(/R\$\s?450/)).toBeInTheDocument();
    expect(within(paidRow).getAllByText('—')).toHaveLength(1);
    const pendingRow = screen.getByText('Adiantamento obra').closest('tr')!;
    expect(within(pendingRow).getAllByText('—')).toHaveLength(2);
  });

  it('esconde a coluna/ação "Registrar despesa" sem payables:write, mantendo a leitura', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read'];
    renderTab();
    // A lista (leitura) segue visível…
    expect(screen.getByText('Adiantamento obra')).toBeInTheDocument();
    // …mas a ação de contabilizar (payables:write) não aparece.
    expect(screen.queryByRole('button', { name: 'Registrar despesa' })).not.toBeInTheDocument();
  });
});
