import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // initialize react-i18next so translated text renders (pt-BR).

// Define mock data via vi.hoisted so the factory closure captures a stable reference,
// even after vi.mock() hoisting reorders execution above the import block.
const { mockItems, mockEmpty } = vi.hoisted(() => {
  return {
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

// Controls which dataset is returned by the mock
let useEmptyData = false;

vi.mock('../../queries', () => ({
  useRemittances: () => ({
    data: useEmptyData
      ? { items: mockEmpty, page: 1, pageSize: 20, total: 0, totalPages: 1 }
      : { items: mockItems, page: 1, pageSize: 20, total: mockItems.length, totalPages: 1 },
    isLoading: false,
  }),
  useCreateRemittance: () => ({ mutate: vi.fn(), isPending: false }),
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

import { RemessasPage } from './list';

describe('RemessasPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useEmptyData = false;
  });

  it('renders the page title', () => {
    renderWithProviders(<RemessasPage />);
    expect(screen.getAllByText('Remessas CNAB240').length).toBeGreaterThan(0);
  });

  it('renders rows for each remittance with sequence number, total brl, and status label', () => {
    renderWithProviders(<RemessasPage />);

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
    renderWithProviders(<RemessasPage />);
    expect(screen.getByText('Nenhuma remessa')).toBeTruthy();
  });

  it('does not show empty state when remittances exist', () => {
    renderWithProviders(<RemessasPage />);
    expect(screen.queryByText('Nenhuma remessa')).toBeNull();
  });
});
