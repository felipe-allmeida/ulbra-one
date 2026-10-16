import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // initialize react-i18next so translated text renders (pt-BR).

// Define mock data via vi.hoisted so the factory closure captures a stable reference,
// even after vi.mock() hoisting reorders execution above the import block.
const { mockItems, mockEmpty, navigateSpy } = vi.hoisted(() => {
  return {
    navigateSpy: vi.fn(),
    mockItems: [
      {
        id: 'rem_00000000000000000000000000000001',
        sequenceNumber: 1,
        status: 'Generated',
        lineCount: 5,
        totalAmount: 12500.75,
        generatedAt: '2026-06-01T10:00:00Z',
      },
      {
        id: 'rem_00000000000000000000000000000002',
        sequenceNumber: 2,
        status: 'Cancelled',
        lineCount: 2,
        totalAmount: 3000.0,
        generatedAt: '2026-06-02T15:30:00Z',
      },
    ],
    mockEmpty: [] as unknown[],
  };
});

// RemittanceDetail navigates to the Payable on row click; renderWithProviders has no Router,
// so stub useNavigate with a spy (mirrors payables/detail.test.tsx).
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigateSpy };
});

// Controls which dataset is returned by the mock
let useEmptyData = false;

vi.mock('../../queries', () => ({
  useRemittances: () => ({
    data: useEmptyData
      ? { items: mockEmpty, page: 1, pageSize: 20, total: 0, totalPages: 1 }
      : { items: mockItems, page: 1, pageSize: 20, total: mockItems.length, totalPages: 1 },
    isLoading: false,
  }),
  useRemittance: (id: string | undefined) => ({
    data: id
      ? {
          id,
          sequenceNumber: 1,
          status: 'Generated',
          generatedAt: '2026-06-01T10:00:00Z',
          lines: [
            {
              id: 'line_deep',
              payableId: 'pay_1',
              installmentId: 'ins_1',
              method: 'Ted',
              amount: 5000,
              favoredName: 'Fornecedor Deep',
              favoredDocument: '12345678000195',
              reference: 'REF-DEEP',
              status: 'Pending',
              effectiveDate: null,
              bankReference: null,
            },
          ],
        }
      : undefined,
    isLoading: false,
  }),
  useCreateRemittance: () => ({ mutate: vi.fn(), isPending: false }),
  useImportRemittanceReturn: () => ({ mutate: vi.fn(), isPending: false }),
  useConfirmRemittanceLine: () => ({ mutate: vi.fn(), isPending: false }),
  useMarkRemittanceLineReturned: () => ({ mutate: vi.fn(), isPending: false }),
  useBankAccounts: () => ({ data: [] }),
  useCompanies: () => ({ data: [{ id: 'c1', legalName: 'Empresa Teste' }] }),
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
}));

vi.mock('../../store', () => ({
  useApp: () => ({
    empresa: { id: 'c1', legalName: 'Empresa Teste' },
    empresaId: 'c1',
    empresas: [{ id: 'c1', legalName: 'Empresa Teste' }],
    setEmpresaId: vi.fn(),
  }),
}));

// Mock api.remittanceFileUrl so it doesn't blow up without BASE_URL
vi.mock('../../api', () => ({
  api: {
    remittanceFileUrl: (id: string) => `/api/remittances/${id}/file`,
  },
  auth: { token: null },
  getApiBaseUrl: () => 'http://localhost:5001/api',
}));

import { RemittancesTab } from './list';

describe('RemessasPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useEmptyData = false;
  });

  it('renders the page title', () => {
    renderWithProviders(<RemittancesTab companyId='c1' />);
    expect(screen.getAllByText('Remessas CNAB240').length).toBeGreaterThan(0);
  });

  it('renders rows for each remittance with sequence number, total brl, and status label', () => {
    renderWithProviders(<RemittancesTab companyId='c1' />);

    // Sequence numbers (rendered as text '1' and '2' in the first column)
    expect(screen.getAllByText('1').length).toBeGreaterThan(0);
    expect(screen.getAllByText('2').length).toBeGreaterThan(0);

    // Total amounts formatted in BRL with cents
    expect(screen.getByText(/12\.500,75/)).toBeTruthy();
    expect(screen.getByText(/3\.000,00/)).toBeTruthy();

    // Status labels (pt-BR translations)
    expect(screen.getByText('Gerada')).toBeTruthy();
    expect(screen.getByText('Cancelada')).toBeTruthy();
  });

  it('shows empty state message when no remittances', () => {
    useEmptyData = true;
    renderWithProviders(<RemittancesTab companyId='c1' />);
    expect(screen.getByText('Nenhuma remessa')).toBeTruthy();
  });

  it('does not show empty state when remittances exist', () => {
    renderWithProviders(<RemittancesTab companyId='c1' />);
    expect(screen.queryByText('Nenhuma remessa')).toBeNull();
  });

  it('opens the detail modal when focusRemittanceId is set (?remittance= deep link)', () => {
    renderWithProviders(<RemittancesTab companyId='c1' focusRemittanceId='rem_00000000000000000000000000000001' />);
    // The deep-linked remittance's line is visible without any click.
    expect(screen.getByText('REF-DEEP')).toBeTruthy();
    expect(screen.getByText('Fornecedor Deep')).toBeTruthy();
  });

  /**
   * A coluna de ações parava a propagação inteira: o botão ficava isolado, mas o espaço em volta
   * dele — e a célula toda numa linha sem ação disponível — virava zona morta.
   */
  it('navigates from the actions column when the click misses the button', () => {
    renderWithProviders(<RemittancesTab companyId='c1' focusRemittanceId='rem_00000000000000000000000000000001' />);
    const cells = screen.getByText('REF-DEEP').closest('tr')!.querySelectorAll('td');
    fireEvent.click(cells[cells.length - 1]);
    expect(navigateSpy).toHaveBeenCalledWith('/app/payables/pay_1', {
      state: { from: '/app/treasury?tab=remittances&remittance=rem_00000000000000000000000000000001' },
    });
  });

  it('navigates to the line Payable when the row is clicked, with origin back to this remittance', () => {
    renderWithProviders(<RemittancesTab companyId='c1' focusRemittanceId='rem_00000000000000000000000000000001' />);
    fireEvent.click(screen.getByText('REF-DEEP'));
    expect(navigateSpy).toHaveBeenCalledWith('/app/payables/pay_1', {
      state: { from: '/app/treasury?tab=remittances&remittance=rem_00000000000000000000000000000001' },
    });
  });
});
