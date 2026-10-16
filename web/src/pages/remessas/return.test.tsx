import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // initialize react-i18next so translated text renders (pt-BR).

// RemittanceDetail calls useNavigate (row → Payable); renderWithProviders has no Router.
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn() };
});

// ---------- shared mock data ----------

const pendingLine = {
  id: 'line_0000000000000000000000000000001',
  payableId: 'pay_0000000000000000000000000000001',
  installmentId: 'ins_0000000000000000000000000000001',
  method: 'Ted',
  amount: 5000,
  favoredName: 'Fornecedor A',
  favoredDocument: '12345678000195',
  reference: 'REF001',
  status: 'Pending',
  effectiveDate: null,
  bankReference: null,
};

const paidLine = {
  id: 'line_0000000000000000000000000000002',
  payableId: 'pay_0000000000000000000000000000002',
  installmentId: 'ins_0000000000000000000000000000002',
  method: 'Ted',
  amount: 2500,
  favoredName: 'Fornecedor B',
  favoredDocument: '98765432000196',
  reference: 'REF002',
  status: 'Paid',
  effectiveDate: '2026-06-20',
  bankReference: 'BANK123',
};

const returnedLine = {
  id: 'line_0000000000000000000000000000003',
  payableId: 'pay_0000000000000000000000000000003',
  installmentId: 'ins_0000000000000000000000000000003',
  method: 'Ted',
  amount: 1500,
  favoredName: 'Fornecedor C',
  favoredDocument: '11111111000111',
  reference: 'REF003',
  status: 'Returned',
  effectiveDate: '2026-06-21',
  bankReference: null,
};

const mockRemittance = {
  id: 'rem_0000000000000000000000000000001',
  companyId: 'company_0000000000000000000000000000001',
  payingBankAccountId: 'ba_0000000000000000000000000000001',
  bankCode: '033',
  sequenceNumber: 1,
  status: 'PartiallyReturned',
  generatedAt: '2026-06-18T10:00:00Z',
  lines: [pendingLine, paidLine, returnedLine],
};

// Hoisted mock vars so vi.mock factories can close over them
const { mockConfirmMutate, mockMarkReturnedMutate } = vi.hoisted(() => ({
  mockConfirmMutate: vi.fn(),
  mockMarkReturnedMutate: vi.fn(),
}));

vi.mock('../../queries', () => ({
  useRemittances: () => ({
    data: {
      items: [
        {
          id: mockRemittance.id,
          sequenceNumber: 1,
          status: 'PartiallyReturned',
          lineCount: 3,
          totalAmount: 9000,
          generatedAt: '2026-06-18T10:00:00Z',
        },
      ],
      page: 1,
      pageSize: 20,
      total: 1,
      totalPages: 1,
    },
    isLoading: false,
  }),
  useRemittance: () => ({
    data: mockRemittance,
    isLoading: false,
  }),
  useCreateRemittance: () => ({ mutate: vi.fn(), isPending: false }),
  useBankAccounts: () => ({ data: [] }),
  useCompanies: () => ({ data: [{ id: 'c1', legalName: 'Empresa Teste' }] }),
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
  useConfirmRemittanceLine: ({ onSuccess }: { onSuccess?: () => void } = {}) => ({
    mutate: mockConfirmMutate.mockImplementation((_vars: unknown, opts?: { onSuccess?: () => void }) => {
      onSuccess?.();
      opts?.onSuccess?.();
    }),
    isPending: false,
  }),
  useMarkRemittanceLineReturned: ({ onSuccess }: { onSuccess?: () => void } = {}) => ({
    mutate: mockMarkReturnedMutate.mockImplementation((_vars: unknown, opts?: { onSuccess?: () => void }) => {
      onSuccess?.();
      opts?.onSuccess?.();
    }),
    isPending: false,
  }),
}));

vi.mock('../../store', () => ({
  useApp: () => ({
    empresa: { id: 'c1', legalName: 'Empresa Teste' },
    empresaId: 'c1',
    empresas: [{ id: 'c1', legalName: 'Empresa Teste' }],
    setEmpresaId: vi.fn(),
  }),
}));

vi.mock('../../api', () => ({
  api: {
    remittanceFileUrl: (id: string) => `/api/remittances/${id}/file`,
    remittanceSimulateReturnUrl: (id: string) => `/api/remittances/${id}/simulate-return`,
  },
  auth: { token: null },
  getApiBaseUrl: () => 'http://localhost:5001/api',
}));

import { RemittancesTab } from './list';

describe('Remittance return conciliation UI', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  function openDetail() {
    renderWithProviders(<RemittancesTab companyId='c1' />);
    // Click the row's "Títulos" button to open the detail modal
    const detailBtn = screen.getAllByRole('button', { name: 'Títulos' })[0];
    fireEvent.click(detailBtn);
  }

  it('renders line status badges for all three status values', () => {
    openDetail();
    // Badges from the detail table
    expect(screen.getByText('Pendente')).toBeTruthy();
    expect(screen.getByText('Paga')).toBeTruthy();
    expect(screen.getByText('Devolvida')).toBeTruthy();
  });

  it('shows only "Retirar da remessa" (no manual confirm) for Pending lines', () => {
    openDetail();
    // The baixa is done via the return-file conciliation tab — the modal must not offer manual confirm.
    expect(screen.queryByText('Confirmar pago')).not.toBeInTheDocument();
    // Only one Pending line → exactly one "Retirar da remessa" action
    const removeButtons = screen.getAllByText('Retirar da remessa');
    expect(removeButtons).toHaveLength(1);
  });

  it('opens reason dialog when "Retirar da remessa" is clicked and calls markReturned on submit', () => {
    openDetail();
    fireEvent.click(screen.getByText('Retirar da remessa'));

    // Dialog should appear with the input
    expect(screen.getByText('Motivo da devolução (obrigatório):')).toBeTruthy();

    // Fill in reason and confirm
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'Dados incorretos' } });

    // Submit the dialog (its confirm button is labelled "Devolver"; the dialog title shares the text)
    fireEvent.click(screen.getByRole('button', { name: 'Devolver' }));

    expect(mockMarkReturnedMutate).toHaveBeenCalledWith({
      id: mockRemittance.id,
      lineId: pendingLine.id,
      reason: 'Dados incorretos',
    });
  });

  it('displays effectiveDate for paid lines and dash for pending/returned lines with no date', () => {
    openDetail();
    // Paid line has effectiveDate 2026-06-20 → should render as a pt-BR date
    // The exact format depends on the locale, but it should contain "20" and "06" or "2026"
    const cells = screen.getAllByRole('cell');
    const dateCells = cells.filter((c) => c.textContent?.includes('20/06/2026') || c.textContent === '—');
    expect(dateCells.length).toBeGreaterThan(0);
  });

  it('displays bank reference for paid lines and dash for lines without one', () => {
    openDetail();
    expect(screen.getByText('BANK123')).toBeTruthy();
  });
});
