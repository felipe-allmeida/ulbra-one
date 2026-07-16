import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../test/renderWithProviders';

// Gating in-view dos Cadastros (Registry): criar/editar/ativar/excluir e o Salvar dos
// formulários são backed por registry:write. Sem a permissão, só leitura.
const { permState } = vi.hoisted(() => ({ permState: { canWrite: false } }));

vi.mock('../permissions', async (orig) => ({
  ...(await orig<typeof import('../permissions')>()),
  usePermissions: () => ({
    can: () => permState.canWrite,
    canAny: () => permState.canWrite,
    me: null,
    isAdmin: false,
  }),
}));
vi.mock('../api', async (orig) => {
  const actual = await orig<typeof import('../api')>();
  return { ...actual, api: { lookupCnpj: vi.fn(), lookupCep: vi.fn(), createVendor: vi.fn() } };
});
vi.mock('../store', () => ({
  useApp: () => ({ empresa: { id: 'c1', legalName: 'ACME' }, empresaId: 'c1' }),
}));
vi.mock('./shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../queries', async (orig) => {
  const actual = await orig<typeof import('../queries')>();
  return {
    ...actual,
    useVendors: () => ({
      data: {
        items: [{ id: 'v1', name: 'Fornecedor X', personType: 'Company', document: '11222333000181', active: true }],
        total: 1,
        totalPages: 1,
      },
      isLoading: false,
    }),
    useVendorAction: () => ({ mutate: vi.fn(), isPending: false }),
    useVendor: () => ({ data: undefined }),
    useSaveVendor: () => ({ mutate: vi.fn(), isPending: false }),
  };
});

import { FornecedoresPage } from './fornecedores';
import { VendorForm } from './registry-forms';

beforeEach(() => {
  permState.canWrite = false;
});

describe('FornecedoresPage — gating (registry:write)', () => {
  it('read-only: sem "Novo fornecedor" e sem ações de linha', () => {
    renderWithProviders(<MemoryRouter><FornecedoresPage /></MemoryRouter>);
    expect(screen.queryByRole('button', { name: 'Novo fornecedor' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Excluir' })).not.toBeInTheDocument();
    // A lista continua visível (leitura preservada).
    expect(screen.getByText('Fornecedor X')).toBeInTheDocument();
  });

  it('com registry:write: mostra criar e ações de linha', () => {
    permState.canWrite = true;
    renderWithProviders(<MemoryRouter><FornecedoresPage /></MemoryRouter>);
    expect(screen.getByRole('button', { name: 'Novo fornecedor' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Excluir' })).toBeInTheDocument();
  });
});

describe('VendorForm — Salvar gated por registry:write', () => {
  it('read-only esconde Criar e mostra Fechar', () => {
    renderWithProviders(<VendorForm companyId='c1' onSaved={() => {}} onCancel={() => {}} />);
    expect(screen.queryByRole('button', { name: 'Criar' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Fechar' })).toBeInTheDocument();
  });

  it('com registry:write mostra Criar', () => {
    permState.canWrite = true;
    renderWithProviders(<VendorForm companyId='c1' onSaved={() => {}} onCancel={() => {}} />);
    expect(screen.getByRole('button', { name: 'Criar' })).toBeInTheDocument();
  });
});
