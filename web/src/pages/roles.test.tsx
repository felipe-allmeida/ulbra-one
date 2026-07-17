import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import '../i18n';

const { mockUseRoles, mockUseCatalog, mockMutate, mockSuccess, mockError } = vi.hoisted(() => ({
  mockUseRoles: vi.fn(),
  mockUseCatalog: vi.fn(),
  mockMutate: vi.fn(),
  mockSuccess: vi.fn(),
  mockError: vi.fn(),
}));

vi.mock('../queries', () => ({
  useRoles: mockUseRoles,
  usePermissionCatalog: mockUseCatalog,
  useSetRolePermissions: () => ({ mutate: mockMutate, isPending: false }),
}));
vi.mock('../toast', () => ({ useToast: () => ({ success: mockSuccess, error: mockError }) }));

import { RolesPage } from './roles';

const CATALOG = ['payables:read', 'payables:write', 'accounting:read'];

beforeEach(() => {
  mockMutate.mockReset();
  mockUseCatalog.mockReturnValue({ data: CATALOG, isLoading: false });
  mockUseRoles.mockReturnValue({
    data: [
      { id: 'role_admin', name: 'Admin', permissions: [] },
      { id: 'role_fin', name: 'Finance', permissions: ['payables:read'] },
    ],
    isLoading: false,
  });
});

describe('RolesPage', () => {
  it('renders the matrix with editable roles and hides Admin as a column', () => {
    render(<RolesPage />);
    expect(screen.getByText('Cargos & permissões')).toBeInTheDocument();
    // O cabeçalho da coluna usa o rótulo em português, não o nome técnico em inglês.
    expect(screen.getByText('Financeiro')).toBeInTheDocument();
    expect(screen.queryByText('Finance')).not.toBeInTheDocument();
    // Admin is described as a bypass note, never as a matrix column with a checkbox.
    expect(screen.queryByRole('checkbox', { name: /^Admin —/ })).not.toBeInTheDocument();
    expect(screen.getByText(/acesso total/i)).toBeInTheDocument();
  });

  it('reflects the role\'s current permissions as checked boxes', () => {
    render(<RolesPage />);
    expect(screen.getByRole('checkbox', { name: 'Financeiro — payables:read' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Financeiro — payables:write' })).not.toBeChecked();
  });

  it('enables Save only after an edit and submits the reconciled permission set', () => {
    render(<RolesPage />);
    const saveBtn = screen.getByRole('button', { name: /Salvar/ });
    expect(saveBtn).toBeDisabled();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Financeiro — payables:write' }));
    expect(saveBtn).toBeEnabled();

    fireEvent.click(saveBtn);
    expect(mockMutate).toHaveBeenCalledTimes(1);
    const payload = mockMutate.mock.calls[0][0] as { id: string; permissions: string[] };
    expect(payload.id).toBe('role_fin');
    expect([...payload.permissions].sort()).toEqual(['payables:read', 'payables:write']);
  });

  it('groups permissions by resource', () => {
    render(<RolesPage />);
    expect(screen.getByText('Contas a pagar')).toBeInTheDocument();
    expect(screen.getByText('Contábil')).toBeInTheDocument();
  });

  it('shows a loading placeholder while data is pending', () => {
    mockUseRoles.mockReturnValue({ data: undefined, isLoading: true });
    render(<RolesPage />);
    expect(screen.getByText('Carregando…')).toBeInTheDocument();
  });
});
