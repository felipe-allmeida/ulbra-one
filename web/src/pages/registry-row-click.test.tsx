import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';

/**
 * As telas de cadastro têm a mesma zona morta corrigida no #240, na pior variante: a coluna de
 * ações para a propagação INTEIRA, e o conteúdo dela é gated por permissão de escrita. Para quem
 * só lê, a célula renderiza vazia e ainda engole o clique — a linha abre em toda a sua largura
 * menos ali, sem nada que explique a diferença.
 */
const navigate = vi.fn();
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigate, useSearchParams: () => [new URLSearchParams(), vi.fn()] };
});

const { canWrite } = vi.hoisted(() => ({ canWrite: { value: true } }));
vi.mock('../permissions', async (orig) => ({
  ...(await orig<typeof import('../permissions')>()),
  usePermissions: () => ({
    can: () => canWrite.value,
    canAny: () => canWrite.value,
    me: { isAdmin: true },
    isAdmin: true,
  }),
}));
vi.mock('../store', async (orig) => ({
  ...(await orig<typeof import('../store')>()),
  useApp: () => ({
    empresaId: 'c1',
    empresa: { id: 'c1', legalName: 'ULBRA' },
    empresas: [{ id: 'c1', legalName: 'ULBRA' }],
    scopeCompanyId: 'c1',
    isAllCompanies: false,
  }),
}));
vi.mock('../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

const page = <T,>(items: T[]) => ({ data: { items, total: items.length, totalPages: 1 }, isLoading: false });

vi.mock('../queries', () => ({
  useVendors: () =>
    page([
      {
        id: 'v1',
        name: 'Papelaria Central',
        document: '11222333000181',
        personType: 1,
        active: true,
        email: null,
        phone: null,
      },
    ]),
  useVendor: () => ({ data: null, isLoading: false }),
  useVendorAction: () => ({ mutate: vi.fn(), isPending: false }),
  useCustomers: () =>
    page([
      { id: 'cu1', name: 'Cliente A', document: '11222333000181', personType: 1, active: true, creditLimit: null },
    ]),
  useCustomerAction: () => ({ mutate: vi.fn(), isPending: false }),
  useEmployees: () =>
    page([{ id: 'e1', name: 'Maria Silva', document: '12345678901', role: 'Analista', unitId: 'u1', active: true }]),
  useEmployeeAction: () => ({ mutate: vi.fn(), isPending: false }),
  useUnits: () => ({ data: [{ id: 'u1', acronym: 'CAN', name: 'Canoas' }] }),
  useCostCenters: () => ({ data: [] }),
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
  // Hooks dos formulários que o drawer monta ao abrir.
  useSaveCustomer: () => ({ mutate: vi.fn(), isPending: false }),
  useSaveEmployee: () => ({ mutate: vi.fn(), isPending: false }),
  useSaveVendor: () => ({ mutate: vi.fn(), isPending: false }),
  useLedgerAccounts: () => ({ data: [], isLoading: false }),
  useWithholdingPreview: () => ({ data: null, isLoading: false }),
}));

import { FornecedoresPage } from './fornecedores';
import { ClientesPage } from './clientes';
import { ColaboradoresPage } from './colaboradores';

const renderPage = (ui: React.ReactElement) => render(<MemoryRouter initialEntries={['/app']}>{ui}</MemoryRouter>);

/** O último `<td>` da linha é a coluna de ações. */
const actionCellOf = (text: string) => {
  const cells = screen.getByText(text).closest('tr')!.querySelectorAll('td');
  return cells[cells.length - 1];
};

describe('Fornecedores — coluna de ações', () => {
  beforeEach(() => {
    navigate.mockClear();
    canWrite.value = true;
  });

  it('abre o fornecedor quando o clique cai na coluna de ações, fora dos botões', () => {
    renderPage(<FornecedoresPage />);

    fireEvent.click(actionCellOf('Papelaria Central'));

    expect(navigate).toHaveBeenCalledWith('/app/vendors/v1', expect.anything());
  });

  it('abre o fornecedor pela coluna de ações mesmo sem permissão de escrita, quando ela renderiza vazia', () => {
    canWrite.value = false;
    renderPage(<FornecedoresPage />);

    fireEvent.click(actionCellOf('Papelaria Central'));

    expect(navigate).toHaveBeenCalledWith('/app/vendors/v1', expect.anything());
  });

  it('não abre o fornecedor quando o clique cai num botão de ação', () => {
    renderPage(<FornecedoresPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Editar' }));

    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith('/app/vendors/v1', expect.anything());
  });
});

describe('Clientes — coluna de ações', () => {
  beforeEach(() => {
    canWrite.value = false;
  });

  it('abre o drawer de edição quando o clique cai na coluna de ações vazia', () => {
    renderPage(<ClientesPage />);

    fireEvent.click(actionCellOf('Cliente A'));

    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});

describe('Colaboradores — coluna de ações', () => {
  beforeEach(() => {
    canWrite.value = false;
  });

  it('abre o drawer de edição quando o clique cai na coluna de ações vazia', () => {
    renderPage(<ColaboradoresPage />);

    fireEvent.click(actionCellOf('Maria Silva'));

    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});
