import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // pt-BR labels.

// RemittanceDetail calls useNavigate (row → Payable); renderWithProviders has no Router.
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn() };
});

// Remittance in-view gating: generate remittance and remove-from-remittance are backed by
// treasury:write. Without the permission the screen is read-only.
const { permState, rem } = vi.hoisted(() => ({
  permState: { canWrite: false },
  rem: {
    id: 'rem_1',
    companyId: 'c1',
    sequenceNumber: 1,
    status: 'Generated',
    generatedAt: '2026-06-18T10:00:00Z',
    lines: [
      {
        id: 'line_1',
        payableId: 'pay_1',
        installmentId: 'ins_1',
        method: 'Ted',
        amount: 5000,
        favoredName: 'Fornecedor A',
        favoredDocument: '12345678000195',
        reference: 'REF001',
        status: 'Pending',
        effectiveDate: null,
        bankReference: null,
      },
    ],
  },
}));

vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof import('../../permissions')>()),
  usePermissions: () => ({
    can: () => permState.canWrite,
    canAny: () => permState.canWrite,
    me: null,
    isAdmin: false,
  }),
}));
vi.mock('../../queries', () => {
  const noop = () => ({ mutate: vi.fn(), isPending: false });
  return {
    useRemittances: () => ({
      data: {
        items: [
          {
            id: rem.id,
            sequenceNumber: 1,
            status: 'Generated',
            lineCount: 1,
            totalAmount: 5000,
            generatedAt: rem.generatedAt,
          },
        ],
        page: 1,
        pageSize: 20,
        total: 1,
        totalPages: 1,
      },
      isLoading: false,
    }),
    useRemittance: () => ({ data: rem, isLoading: false }),
    useCreateRemittance: noop,
    useBankAccounts: () => ({ data: [] }),
    useCompanies: () => ({ data: [{ id: 'c1', legalName: 'Empresa Teste' }] }),
    useMe: () => ({ data: null }),
    useImportRemittanceReturn: noop,
    useConfirmRemittanceLine: noop,
    useMarkRemittanceLineReturned: noop,
  };
});
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

const openDetail = () => {
  renderWithProviders(<RemittancesTab companyId='c1' />);
  fireEvent.click(screen.getAllByRole('button', { name: 'Títulos' })[0]);
};

beforeEach(() => {
  permState.canWrite = false;
});

describe('Remessa — gating (treasury:write)', () => {
  it('read-only: sem Gerar remessa nem Retirar da remessa', () => {
    openDetail();
    expect(screen.queryByText('Gerar remessa')).not.toBeInTheDocument();
    expect(screen.queryByText('Retirar da remessa')).not.toBeInTheDocument();
    // The modal never offers manual baixa — that lives only in the return conciliation tab.
    expect(screen.queryByText('Confirmar pago')).not.toBeInTheDocument();
    // Read access preserved: the detail modal opens (its titles-table heading is visible).
    expect(screen.getByText('Títulos da remessa')).toBeInTheDocument();
  });

  it('com treasury:write: mostra Gerar remessa e Retirar da remessa (sem confirmar manual)', () => {
    permState.canWrite = true;
    openDetail();
    expect(screen.getByText('Gerar remessa')).toBeInTheDocument();
    expect(screen.getByText('Retirar da remessa')).toBeInTheDocument();
    expect(screen.queryByText('Confirmar pago')).not.toBeInTheDocument();
  });
});
