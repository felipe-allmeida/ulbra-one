import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '../i18n';

const {
  mockUseUsers,
  mockUseRoles,
  mockUseCompanies,
  mockUseAllUnits,
  mockUseAllCostCenters,
  mockSetRole,
  mockSetScope,
  mockSetActive,
  mockSetApprover,
  mockCreateUser,
  mockSuccess,
  mockError,
} = vi.hoisted(() => ({
  mockUseUsers: vi.fn(),
  mockUseRoles: vi.fn(),
  mockUseCompanies: vi.fn(),
  mockUseAllUnits: vi.fn(),
  mockUseAllCostCenters: vi.fn(),
  mockSetRole: vi.fn(),
  mockSetScope: vi.fn(),
  mockSetActive: vi.fn(),
  mockSetApprover: vi.fn(),
  mockCreateUser: vi.fn(),
  mockSuccess: vi.fn(),
  mockError: vi.fn(),
}));

vi.mock('../queries', () => ({
  useUsers: mockUseUsers,
  useRoles: mockUseRoles,
  useCompanies: mockUseCompanies,
  useAllUnits: mockUseAllUnits,
  useAllCostCenters: mockUseAllCostCenters,
  useMe: () => ({ data: undefined }),
  useSetUserRole: () => ({ mutateAsync: mockSetRole, mutate: vi.fn(), isPending: false }),
  useSetUserScope: () => ({ mutateAsync: mockSetScope, mutate: vi.fn(), isPending: false }),
  useSetUserActive: () => ({ mutateAsync: vi.fn().mockResolvedValue({}), mutate: mockSetActive, isPending: false }),
  useSetBudgetAdjustmentApprover: () => ({
    mutateAsync: vi.fn().mockResolvedValue({}),
    mutate: mockSetApprover,
    isPending: false,
  }),
  useCreateUser: () => ({ mutateAsync: mockCreateUser, mutate: vi.fn(), isPending: false }),
}));
vi.mock('../toast', () => ({ useToast: () => ({ success: mockSuccess, error: mockError }) }));

import { UsersPage } from './users';

const ROLES = [
  { id: 'role_req', name: 'Requester', permissions: [] },
  { id: 'role_app', name: 'Approver', permissions: [] },
  { id: 'role_fin', name: 'Finance', permissions: [] },
  { id: 'role_ctl', name: 'Controllership', permissions: [] },
  { id: 'role_admin', name: 'Admin', permissions: [] },
];
const COMPANIES = [{ id: 'company_1', legalName: 'ACME', cnpj: '00000000000000', active: true }];
const UNITS = [
  { id: 'unit_1', companyId: 'company_1', name: 'Matriz', acronym: 'MTZ', cnpj: null, isMatriz: true, active: true },
  { id: 'unit_2', companyId: 'company_1', name: 'Filial', acronym: 'FIL', cnpj: null, isMatriz: false, active: true },
];
const COST_CENTERS = [
  {
    id: 'cc_1',
    companyId: 'company_1',
    code: '100',
    name: 'TI',
    annualBudget: 0,
    autoApprovalLimit: 0,
    active: true,
    unitIds: [],
  },
  {
    id: 'cc_2',
    companyId: 'company_1',
    code: '200',
    name: 'RH',
    annualBudget: 0,
    autoApprovalLimit: 0,
    active: true,
    unitIds: [],
  },
];
const USERS = [
  {
    id: 'user_req',
    email: 'rita@x.com',
    name: 'Rita Requester',
    role: 'Requester',
    isAdmin: false,
    active: true,
    canApproveBudgetAdjustment: false,
    pendingFirstLogin: false,
    companyIds: ['company_1'],
    units: [{ companyId: 'company_1', unitId: 'unit_1' }],
    costCenters: [],
  },
  {
    id: 'user_app',
    email: 'ana@x.com',
    name: 'Ana Aprovadora',
    role: 'Approver',
    isAdmin: false,
    active: true,
    canApproveBudgetAdjustment: false,
    pendingFirstLogin: false,
    companyIds: ['company_1'],
    units: [],
    costCenters: [{ companyId: 'company_1', costCenterId: 'cc_1' }],
  },
  {
    id: 'user_fin',
    email: 'fabio@x.com',
    name: 'Fábio Financeiro',
    role: 'Finance',
    isAdmin: false,
    active: true,
    canApproveBudgetAdjustment: false,
    pendingFirstLogin: false,
    companyIds: [],
    units: [],
    costCenters: [],
  },
  {
    id: 'user_ctl',
    email: 'carla@x.com',
    name: 'Carla Controladoria',
    role: 'Controllership',
    isAdmin: false,
    active: true,
    canApproveBudgetAdjustment: false,
    pendingFirstLogin: false,
    companyIds: [],
    units: [],
    costCenters: [],
  },
  {
    id: 'user_novo',
    email: 'novo@x.com',
    name: 'Nino Novato',
    role: 'Finance',
    isAdmin: false,
    active: true,
    canApproveBudgetAdjustment: false,
    pendingFirstLogin: true,
    companyIds: [],
    units: [],
    costCenters: [],
  },
];

beforeEach(() => {
  vi.clearAllMocks();
  mockSetRole.mockResolvedValue({});
  mockCreateUser.mockResolvedValue({});
  mockSetScope.mockResolvedValue({});
  mockUseRoles.mockReturnValue({ data: ROLES, isLoading: false });
  mockUseCompanies.mockReturnValue({ data: COMPANIES, isLoading: false });
  mockUseAllUnits.mockReturnValue({ data: UNITS, isLoading: false });
  mockUseAllCostCenters.mockReturnValue({ data: COST_CENTERS, isLoading: false });
  mockUseUsers.mockReturnValue({ data: USERS, isLoading: false });
});

describe('UsersPage', () => {
  it('renders one row per user with the pt-BR role select and the active checkbox', () => {
    render(<UsersPage />);
    expect(screen.getByText('Usuários & acessos')).toBeInTheDocument();
    expect(screen.getByText('Rita Requester')).toBeInTheDocument();
    expect(screen.getByText('rita@x.com')).toBeInTheDocument();

    // Role select renders per row with the technical value; the option label is translated.
    expect(screen.getByLabelText('Cargo de Rita Requester')).toHaveValue('Requester');
    expect(screen.getByLabelText('Cargo de Fábio Financeiro')).toHaveValue('Finance');

    // Active checkbox renders per row, reflecting the server flag.
    expect(screen.getByLabelText('Ativo: Rita Requester')).toBeChecked();
  });

  it('flags a pre-registered user who has never signed in', () => {
    render(<UsersPage />);
    expect(screen.getByText('Aguardando 1º acesso')).toBeInTheDocument();
  });

  it('registers a user by e-mail and role from the dialog', async () => {
    render(<UsersPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar usuário' }));

    fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'Novato@Ulbra.br' } });
    fireEvent.change(screen.getByLabelText('Nome'), { target: { value: 'Novato' } });
    fireEvent.change(screen.getByLabelText('Cargo'), { target: { value: 'Finance' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar' }));

    await waitFor(() =>
      expect(mockCreateUser).toHaveBeenCalledWith({
        email: 'Novato@Ulbra.br',
        name: 'Novato',
        role: 'Finance',
        companyIds: [],
        units: [],
        costCenters: [],
        approvalLimit: null,
        canApproveBudgetAdjustment: false,
      }),
    );
  });

  it('registers a scoped user with the scope picked in the dialog', async () => {
    render(<UsersPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar usuário' }));

    fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'solicitante@ulbra.br' } });
    fireEvent.change(screen.getByLabelText('Cargo'), { target: { value: 'Requester' } });

    // Scoped roles get the same three-level editor as the table row.
    fireEvent.click(screen.getByLabelText('Empresa ACME para o novo usuário'));
    fireEvent.click(screen.getByLabelText('Unidade Matriz para o novo usuário'));
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar' }));

    await waitFor(() =>
      expect(mockCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({
          email: 'solicitante@ulbra.br',
          role: 'Requester',
          companyIds: ['company_1'],
          units: [{ companyId: 'company_1', unitId: 'unit_1' }],
          costCenters: [],
        }),
      ),
    );
  });

  it('does not register without an e-mail', () => {
    render(<UsersPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar usuário' }));
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar' }));
    expect(mockCreateUser).not.toHaveBeenCalled();
  });

  it('shows the scope editor toggle for scoped roles and "Vê tudo" (no editor) for the rest', () => {
    render(<UsersPage />);
    // Requester / Approver: an "Editar acessos" toggle exists.
    expect(screen.getByRole('button', { name: 'Editar acessos de Rita Requester' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar acessos de Ana Aprovadora' })).toBeInTheDocument();
    // Finance: no editor toggle; its scope cells read "Vê tudo".
    expect(screen.queryByRole('button', { name: 'Editar acessos de Fábio Financeiro' })).not.toBeInTheDocument();
    expect(screen.getAllByText('Vê tudo').length).toBeGreaterThanOrEqual(3);
  });

  it('toggling the active checkbox saves immediately via setUserActive', () => {
    render(<UsersPage />);
    const cb = screen.getByLabelText('Ativo: Rita Requester');
    fireEvent.click(cb);
    expect(mockSetActive).toHaveBeenCalledWith({ id: 'user_req', active: false }, expect.anything());
  });

  it('shows the "Aprova Adequação" toggle only for Controladoria and saves via setBudgetAdjustmentApprover', () => {
    render(<UsersPage />);
    // Controladoria: the per-user approve toggle is present, reflecting the server flag (false).
    const cb = screen.getByLabelText('Aprova Adequação: Carla Controladoria');
    expect(cb).not.toBeChecked();
    // Other roles never get the toggle (adequação approval is a Controladoria-only grant).
    expect(screen.queryByLabelText('Aprova Adequação: Rita Requester')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Aprova Adequação: Fábio Financeiro')).not.toBeInTheDocument();
    // Toggling saves immediately (independent of the role/scope Salvar).
    fireEvent.click(cb);
    expect(mockSetApprover).toHaveBeenCalledWith({ id: 'user_ctl', canApprove: true }, expect.anything());
  });

  it('expands the Approver: three groups, cost center pre-checked from the server', () => {
    render(<UsersPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Editar acessos de Ana Aprovadora' }));

    // Each group renders its checkboxes; the server-granted company + cost center are pre-checked.
    expect(screen.getByLabelText('Empresa ACME para Ana Aprovadora')).toBeChecked();
    expect(screen.getByLabelText('Unidade Matriz para Ana Aprovadora')).not.toBeChecked();
    expect(screen.getByLabelText('Natureza orçamentária TI para Ana Aprovadora')).toBeChecked();
  });

  it('"Selecionar todos" snapshots every item, and Salvar posts the explicit scope', async () => {
    render(<UsersPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Editar acessos de Rita Requester' }));

    // No dirt yet → no Salvar button in the DOM.
    expect(screen.queryByRole('button', { name: /Salvar/ })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Selecionar todas as empresas para Rita Requester' }));
    fireEvent.click(screen.getByRole('button', { name: 'Selecionar todas as unidades para Rita Requester' }));
    fireEvent.click(
      screen.getByRole('button', { name: 'Selecionar todas as naturezas orçamentárias para Rita Requester' }),
    );

    fireEvent.click(screen.getByRole('button', { name: /Salvar/ }));

    await waitFor(() =>
      expect(mockSetScope).toHaveBeenCalledWith({
        id: 'user_req',
        companyIds: ['company_1'],
        units: [
          { companyId: 'company_1', unitId: 'unit_1' },
          { companyId: 'company_1', unitId: 'unit_2' },
        ],
        costCenters: [
          { companyId: 'company_1', costCenterId: 'cc_1' },
          { companyId: 'company_1', costCenterId: 'cc_2' },
        ],
      }),
    );
    // Role unchanged → no role mutation.
    expect(mockSetRole).not.toHaveBeenCalled();
  });

  it('unchecking a company drops its units and cost centers from the draft', async () => {
    render(<UsersPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Editar acessos de Ana Aprovadora' }));

    // Ana starts with cc_1 granted; unchecking the company clears it.
    fireEvent.click(screen.getByLabelText('Empresa ACME para Ana Aprovadora'));
    fireEvent.click(screen.getByRole('button', { name: /Salvar/ }));

    await waitFor(() =>
      expect(mockSetScope).toHaveBeenCalledWith({
        id: 'user_app',
        companyIds: [],
        units: [],
        costCenters: [],
      }),
    );
  });

  it('shows a loading placeholder while data is pending', () => {
    mockUseUsers.mockReturnValue({ data: undefined, isLoading: true });
    render(<UsersPage />);
    expect(screen.getByText('Carregando')).toBeInTheDocument();
  });

  it('shows an empty state when there are no companies', () => {
    mockUseCompanies.mockReturnValue({ data: [], isLoading: false });
    render(<UsersPage />);
    expect(screen.getByText('Nenhuma empresa cadastrada')).toBeInTheDocument();
  });
});
