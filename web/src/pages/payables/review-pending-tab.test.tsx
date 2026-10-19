import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageItems, mockUseLedgerAccounts, mockMe } = vi.hoisted(() => ({
  mockUseStageItems: vi.fn(),
  mockUseLedgerAccounts: vi.fn(),
  mockMe: { isAdmin: true, permissions: [] as string[] },
}));
vi.mock('../../queries', () => ({
  usePayableStageItems: mockUseStageItems,
  useLedgerAccounts: mockUseLedgerAccounts,
  useMe: () => ({ data: mockMe }),
  useRequestFiscalDocument: () => ({ mutate: vi.fn(), isPending: false }),
}));
vi.mock('../../store', () => ({ useApp: () => ({ empresas: [{ id: 'company_1', legalName: 'ACME' }] }) }));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));
vi.mock('./classification-drawer', () => ({
  ClassificationDrawer: ({ payableId }: { payableId: string }) => (
    <div data-testid='classification-drawer'>{payableId}</div>
  ),
}));

import { ReviewPendingTab } from './review-pending-tab';

const items = [
  {
    payableId: 'pay_1',
    companyId: 'company_1',
    counterpartyName: 'Fornecedor A',
    description: 'Serviço de limpeza',
    costCenterId: 'cc_1',
    categoryId: null,
    firstDueDate: '2026-08-10',
    installmentCount: 3,
    totalAmount: 1200,
    totalWithheld: 0,
    netTotal: 1200,
    status: 'Scheduled',
    origin: 'Purchase',
    hasFiscalDocument: false,
    suggestedAccountingAccountId: 'led_1',
    accountingAccountId: null,
    classificationConfidence: 0.72,
    classificationRationale: 'Fornecedor histórico de serviços',
  },
];

beforeEach(() => {
  mockMe.isAdmin = true;
  mockMe.permissions = [];
  mockUseStageItems.mockReturnValue({ data: { items, total: 1, totalPages: 1 } });
  mockUseLedgerAccounts.mockReturnValue({
    data: [{ id: 'led_1', code: '4.1.1', name: 'Serviços de terceiros' }],
  });
});

function renderTab() {
  return render(
    <MemoryRouter>
      <ReviewPendingTab companyId='company_1' />
    </MemoryRouter>,
  );
}

describe('ReviewPendingTab', () => {
  it('queries stage-items with reviewPending=true and no status filter', () => {
    renderTab();
    expect(mockUseStageItems).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', reviewPending: true }),
      expect.anything(),
    );
    const call = mockUseStageItems.mock.calls[0][0];
    expect(call.status).toBeUndefined();
  });

  // Clicking a row ENTERS the título (opens the classification drawer) — the pattern every list in the app
  // follows. It no longer expands, so what used to live in the expanded body is on the line itself.
  const clickFirstRow = () => fireEvent.click(screen.getByText('Fornecedor A'));

  it('carries the suggested account and confidence on the row, with no expanding', () => {
    renderTab();
    expect(screen.getByText(/4\.1\.1 — Serviços de terceiros \(72%\)/)).toBeInTheDocument();
  });

  it('opens the classification drawer straight from the row click', () => {
    renderTab();
    clickFirstRow();
    expect(screen.getByTestId('classification-drawer')).toHaveTextContent('pay_1');
  });

  it('does not enter without payables:write, keeping the list readable', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read'];
    renderTab();
    // The row (and its natureza) reads fine…
    expect(screen.getByText(/Serviço de limpeza/)).toBeInTheDocument();
    // …but clicking it opens nothing.
    clickFirstRow();
    expect(screen.queryByTestId('classification-drawer')).not.toBeInTheDocument();
  });

  // Consolidated "Todas" (AEL-906): each row is prefixed with its company and is read-only.
  it('prefixes the row with its company and does not enter in the consolidated view', () => {
    render(
      <MemoryRouter>
        <ReviewPendingTab companyId='__all__' isAllCompanies />
      </MemoryRouter>,
    );
    expect(screen.getByText(/ACME · Serviço de limpeza/)).toBeInTheDocument();
    clickFirstRow();
    expect(screen.queryByTestId('classification-drawer')).not.toBeInTheDocument();
  });
});
