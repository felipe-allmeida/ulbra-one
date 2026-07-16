import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // pt-BR labels.

// Gating in-view da Remessa: gerar remessa, importar retorno (.RET), confirmar pago e
// devolver linha são backed por treasury:write. Sem a permissão, a tela é somente leitura.
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
        items: [{ id: rem.id, sequenceNumber: 1, status: 'Generated', lineCount: 1, totalAmount: 5000, generatedAt: rem.generatedAt }],
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

import { RemessasPage } from './list';

const openDetail = () => {
  renderWithProviders(<RemessasPage />);
  fireEvent.click(screen.getAllByText('Títulos da remessa')[0]);
};

beforeEach(() => {
  permState.canWrite = false;
});

describe('Remessa — gating (treasury:write)', () => {
  it('read-only: sem Gerar remessa, Importar retorno, Confirmar pago e Devolver', () => {
    openDetail();
    expect(screen.queryByText('Gerar remessa')).not.toBeInTheDocument();
    expect(screen.queryByText('Importar retorno (.RET)')).not.toBeInTheDocument();
    expect(screen.queryByText('Confirmar pago')).not.toBeInTheDocument();
    expect(screen.queryByText('Devolver')).not.toBeInTheDocument();
    // Leitura preservada: a lista/detalhe abrem e o download continua.
    expect(screen.getAllByText('Títulos da remessa').length).toBeGreaterThan(0);
  });

  it('com treasury:write: mostra as ações de linha e importar retorno', () => {
    permState.canWrite = true;
    openDetail();
    expect(screen.getByText('Gerar remessa')).toBeInTheDocument();
    expect(screen.getByText('Importar retorno (.RET)')).toBeInTheDocument();
    expect(screen.getByText('Confirmar pago')).toBeInTheDocument();
    expect(screen.getByText('Devolver')).toBeInTheDocument();
  });
});
