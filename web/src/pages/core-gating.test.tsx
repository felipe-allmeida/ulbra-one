import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';

// Gating in-view do Core (Estrutura/Empresas e Centros de custo): criar/editar/salvar
// são backed por core:write. Sem a permissão, só leitura.
const { permState } = vi.hoisted(() => ({ permState: { canWrite: false } }));
const company = { id: 'c1', legalName: 'ACME', cnpj: '11222333000181', active: true };

vi.mock('../permissions', async (orig) => ({
  ...(await orig<typeof import('../permissions')>()),
  usePermissions: () => ({
    can: () => permState.canWrite,
    canAny: () => permState.canWrite,
    me: null,
    isAdmin: false,
  }),
}));
vi.mock('../store', () => ({
  ALL_COMPANIES: '__all__',
  useApp: () => ({
    empresas: [company],
    empresa: company,
    empresaId: 'c1',
    isAllCompanies: false,
    scopeReady: true,
    setEmpresaId: vi.fn(),
    refreshEmpresas: vi.fn(),
  }),
}));
vi.mock('../queries', async (orig) => {
  const actual = await orig<typeof import('../queries')>();
  const noop = () => ({ mutate: vi.fn(), isPending: false });
  return {
    ...actual,
    useUnits: () => ({
      data: [{ id: 'u1', name: 'Campus X', acronym: 'CX', cnpj: null, isMatriz: false, active: true }],
    }),
    useCreateCompany: noop,
    useCreateUnit: noop,
    useUpdateCompany: noop,
    useUpdateUnit: noop,
    useSaveCostCenter: noop,
  };
});

import { EstruturaPage } from './estrutura';
import { CentroForm } from './shared';

beforeEach(() => {
  permState.canWrite = false;
});

describe('EstruturaPage — gating (core:write)', () => {
  it('read-only: sem Nova empresa / Nova unidade / Editar', () => {
    renderWithProviders(<EstruturaPage />);
    expect(screen.queryByRole('button', { name: 'Nova empresa' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nova unidade' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    // Leitura preservada: as tabelas com os dados continuam.
    expect(screen.getAllByText('ACME').length).toBeGreaterThan(0);
    expect(screen.getByText('Campus X')).toBeInTheDocument();
  });

  it('com core:write: mostra criar e editar', () => {
    permState.canWrite = true;
    renderWithProviders(<EstruturaPage />);
    expect(screen.getByRole('button', { name: 'Nova empresa' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nova unidade' })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Editar' }).length).toBeGreaterThan(0);
  });
});

describe('CentroForm — Salvar gated por core:write', () => {
  it('read-only esconde Salvar e mostra Fechar', () => {
    renderWithProviders(<CentroForm empresa={company} unidades={[]} onSaved={() => {}} onCancel={() => {}} />);
    expect(screen.queryByRole('button', { name: 'Salvar' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Fechar' })).toBeInTheDocument();
  });

  it('com core:write mostra Salvar', () => {
    permState.canWrite = true;
    renderWithProviders(<CentroForm empresa={company} unidades={[]} onSaved={() => {}} onCancel={() => {}} />);
    expect(screen.getByRole('button', { name: 'Salvar' })).toBeInTheDocument();
  });
});
