import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageItems } = vi.hoisted(() => ({ mockUseStageItems: vi.fn() }));
vi.mock('../../queries', () => ({ usePayableStageItems: mockUseStageItems }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));

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
    status: 'Paid',
    origin: 'Advance',
    hasFiscalDocument: false,
  },
];

beforeEach(() => {
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
});
