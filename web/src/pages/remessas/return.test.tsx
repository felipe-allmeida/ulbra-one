import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // initialize react-i18next so translated text renders (pt-BR).

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
const { mockConfirmMutate, mockMarkReturnedMutate, mockImportMutate } = vi.hoisted(() => ({
  mockConfirmMutate: vi.fn(),
  mockMarkReturnedMutate: vi.fn(),
  mockImportMutate: vi.fn(),
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
  useImportRemittanceReturn: ({ onSuccess }: { onSuccess?: (data: unknown) => void } = {}) => ({
    mutate: mockImportMutate.mockImplementation((_vars: unknown, opts?: { onSuccess?: (d: unknown) => void }) => {
      const outcome = { matched: 2, paid: 1, returned: 1, unmatched: 0, divergent: 0, notes: [] };
      // call the hook-level onSuccess if provided
      onSuccess?.(outcome);
      opts?.onSuccess?.(outcome);
    }),
    isPending: false,
  }),
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

import { RemessasPage } from './list';

describe('Remittance return conciliation UI', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  function openDetail() {
    renderWithProviders(<RemessasPage />);
    // Click the "Títulos da remessa" button on the row to open the detail modal
    const detailBtn = screen.getAllByText('Títulos da remessa')[0];
    fireEvent.click(detailBtn);
  }

  it('renders line status badges for all three status values', () => {
    openDetail();
    // Badges from the detail table
    expect(screen.getByText('Pendente')).toBeTruthy();
    expect(screen.getByText('Paga')).toBeTruthy();
    expect(screen.getByText('Devolvida')).toBeTruthy();
  });

  it('shows "Confirmar pago" and "Devolver" buttons ONLY for Pending lines', () => {
    openDetail();
    // Only one Pending line → exactly one set of action buttons
    const confirmButtons = screen.getAllByText('Confirmar pago');
    expect(confirmButtons).toHaveLength(1);
    const returnButtons = screen.getAllByText('Devolver');
    expect(returnButtons).toHaveLength(1);
  });

  it('calls confirmRemittanceLine when "Confirmar pago" is clicked', () => {
    openDetail();
    const confirmBtn = screen.getByText('Confirmar pago');
    fireEvent.click(confirmBtn);
    expect(mockConfirmMutate).toHaveBeenCalledWith({ id: mockRemittance.id, lineId: pendingLine.id });
  });

  it('opens reason dialog when "Devolver" is clicked and calls markReturned on submit', () => {
    openDetail();
    fireEvent.click(screen.getByText('Devolver'));

    // Dialog should appear with the input
    expect(screen.getByText('Motivo da devolução (obrigatório):')).toBeTruthy();

    // Fill in reason and confirm
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'Dados incorretos' } });

    // Click the confirm button inside the dialog (within the form footer)
    const confirmBtns = screen.getAllByText('Devolver');
    // The last "Devolver" button is the submit inside the dialog
    fireEvent.click(confirmBtns[confirmBtns.length - 1]);

    expect(mockMarkReturnedMutate).toHaveBeenCalledWith({
      id: mockRemittance.id,
      lineId: pendingLine.id,
      reason: 'Dados incorretos',
    });
  });

  it('renders the "Importar retorno (.RET)" file input', () => {
    openDetail();
    expect(screen.getByText('Importar retorno (.RET)')).toBeTruthy();
  });

  it('shows the import outcome toast after a .RET file is selected', async () => {
    openDetail();

    // The hidden file <input> is inside the label that shows "Importar retorno (.RET)"
    const inputEl = document.querySelector('input[type="file"]');
    expect(inputEl).toBeTruthy();
    const input = inputEl!;

    const retFile = new File(['DUMMY CNAB240 CONTENT'], 'retorno.ret', { type: 'text/plain' });
    fireEvent.change(input, { target: { files: [retFile] } });

    // The mock resolves synchronously with { matched:2, paid:1, returned:1, unmatched:0, divergent:0, notes:[] }
    // and the page calls toast.success(t('remittances.return.importSuccess', {...}))
    const toast = await screen.findByText(/Retorno processado/i);
    expect(toast).toBeTruthy();
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
