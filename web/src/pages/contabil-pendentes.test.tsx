import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n'; // initialize react-i18next so translated text renders (pt-BR).

// Define mock data via vi.hoisted so the factory closure captures a stable reference,
// even after vi.mock() hoisting reorders execution above the import block.
const { mockItems } = vi.hoisted(() => {
  return {
    mockItems: [
      {
        id: 'pendpost_00000000000000000000000000000001',
        companyId: 'company_00000000000000000000000000000001',
        sourceKey: 'payable-incurral:abc-123',
        kind: 'Incurral' as const,
        amount: 1500,
        reason: 'Código de despesa não mapeado',
        intendedCodes: '9.99.99.99.99',
        createdAt: '2026-06-01T12:00:00Z',
      },
      {
        id: 'pendpost_00000000000000000000000000000002',
        companyId: 'company_00000000000000000000000000000001',
        sourceKey: 'payment:def-456',
        kind: 'Settlement' as const,
        amount: 2500.5,
        reason: 'Conta bancária não mapeada',
        intendedCodes: '1.01.01.02.01',
        createdAt: '2026-06-02T08:30:00Z',
      },
    ],
  };
});

vi.mock('../queries', () => ({
  usePendingPostings: () => ({
    data: { items: mockItems, page: 1, pageSize: 20, total: mockItems.length, totalPages: 1 },
    isLoading: false,
  }),
  useCompanies: () => ({ data: [{ id: 'c1', legalName: 'Empresa Teste' }] }),
}));

vi.mock('../store', () => ({
  useApp: () => ({
    empresa: { id: 'c1', legalName: 'Empresa Teste' },
    empresaId: 'c1',
    empresas: [{ id: 'c1', legalName: 'Empresa Teste' }],
    setEmpresaId: vi.fn(),
  }),
}));

import { ContabilPendentesPage } from './contabil-pendentes';

describe('ContabilPendentesPage (pendentes de contabilização)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the page title', () => {
    renderWithProviders(<ContabilPendentesPage />);
    expect(screen.getByText('Pendentes de contabilização')).toBeTruthy();
  });

  it('renders a row for each pending posting with kind label, amount, and reason', () => {
    renderWithProviders(<ContabilPendentesPage />);

    // Kind labels (pt-BR translations).
    expect(screen.getByText('Apropriação')).toBeTruthy();
    expect(screen.getByText('Liquidação')).toBeTruthy();

    // Amounts in BRL cents format.
    expect(screen.getByText(/1\.500,00/)).toBeTruthy();
    expect(screen.getByText(/2\.500,50/)).toBeTruthy();

    // Reasons.
    expect(screen.getByText('Código de despesa não mapeado')).toBeTruthy();
    expect(screen.getByText('Conta bancária não mapeada')).toBeTruthy();
  });

  it('renders intended codes and source keys', () => {
    renderWithProviders(<ContabilPendentesPage />);
    expect(screen.getByText('9.99.99.99.99')).toBeTruthy();
    expect(screen.getByText('1.01.01.02.01')).toBeTruthy();
    expect(screen.getByText('payable-incurral:abc-123')).toBeTruthy();
    expect(screen.getByText('payment:def-456')).toBeTruthy();
  });

  it('shows rows and not the empty-state when postings exist', () => {
    renderWithProviders(<ContabilPendentesPage />);
    // Row data is present; empty state must NOT be shown.
    expect(screen.queryByText('Sem pendências')).toBeNull();
    // Two rows → two reason cells present.
    expect(screen.getAllByText(/Código de despesa|Conta bancária/)).toHaveLength(2);
  });
});
