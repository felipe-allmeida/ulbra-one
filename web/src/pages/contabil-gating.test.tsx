import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n'; // pt-BR labels.

// In-view action gating for the Contábil module screens. The buttons must mirror
// the backend permissions exactly: ledger accounts + journal drafts → accounting:write;
// post/reverse a journal entry → accounting:post.
const { permState, fx } = vi.hoisted(() => ({
  permState: { me: null as { isAdmin: boolean; permissions: string[] } | null },
  fx: {
    account: {
      id: 'a1',
      code: '1.01.01',
      reducedCode: null,
      natureza: null,
      name: 'Caixa',
      accountGroup: 'Asset',
      normalBalance: 'Debit',
      kind: 'Analytical',
      parentId: null,
      isActive: true,
    },
    draft: {
      id: 'e1',
      number: 1,
      competenceDate: '2026-01-10',
      description: 'Rascunho',
      status: 'Draft',
      totalDebit: 1000,
      reversedByEntryId: null,
    },
    posted: {
      id: 'e2',
      number: 2,
      competenceDate: '2026-01-11',
      description: 'Contabilizado',
      status: 'Posted',
      totalDebit: 2000,
      reversedByEntryId: null,
    },
  },
}));

vi.mock('../queries', () => {
  const noop = () => ({ mutate: vi.fn(), isPending: false, error: null });
  return {
  useLedgerAccounts: () => ({ data: [fx.account], isLoading: false }),
  useSeedLedgerAccounts: noop,
  useUpdateLedgerAccount: noop,
  useCreateLedgerAccount: noop,
  useJournalEntries: () => ({ data: [fx.draft, fx.posted], isLoading: false }),
  usePostJournalEntry: noop,
  useReverseJournalEntry: noop,
  useCreateJournalEntry: noop,
  useUpdateJournalEntry: noop,
  useCostCenters: () => ({ data: [] }),
  useCompanies: () => ({ data: [{ id: 'c1', legalName: 'Empresa Teste' }] }),
  useMe: () => ({ data: permState.me }),
  };
});

vi.mock('../store', () => ({
  useApp: () => ({
    empresa: { id: 'c1', legalName: 'Empresa Teste' },
    empresaId: 'c1',
    empresas: [{ id: 'c1', legalName: 'Empresa Teste' }],
    setEmpresaId: vi.fn(),
  }),
}));

import { ContabilPlanoPage } from './contabil-plano';
import { ContabilLancamentosPage } from './contabil-lancamentos';

const me = (permissions: string[]) => ({ isAdmin: false, permissions });
const btn = (name: string | RegExp) => screen.queryByRole('button', { name });

beforeEach(() => {
  permState.me = null;
});

describe('Plano de contas — gating de ações (accounting:write)', () => {
  it('esconde criar/editar/ativar para quem só tem accounting:read', () => {
    permState.me = me(['accounting:read']);
    renderWithProviders(<ContabilPlanoPage />);
    expect(btn('Nova conta')).not.toBeInTheDocument();
    expect(btn('Editar')).not.toBeInTheDocument();
    expect(btn('Desativar')).not.toBeInTheDocument();
  });

  it('mostra criar/editar/ativar com accounting:write', () => {
    permState.me = me(['accounting:read', 'accounting:write']);
    renderWithProviders(<ContabilPlanoPage />);
    expect(btn('Nova conta')).toBeInTheDocument();
    expect(btn('Editar')).toBeInTheDocument();
    expect(btn('Desativar')).toBeInTheDocument();
  });
});

describe('Lançamentos contábeis — gating de ações (write vs post)', () => {
  it('read-only: só Ver (nenhuma ação de escrita/postagem)', () => {
    permState.me = me(['accounting:read']);
    renderWithProviders(<ContabilLancamentosPage />);
    expect(btn('Novo lançamento')).not.toBeInTheDocument();
    expect(btn('Editar')).not.toBeInTheDocument();
    expect(btn('Contabilizar')).not.toBeInTheDocument();
    expect(btn('Estornar')).not.toBeInTheDocument();
    expect(btn('Ver')).toBeInTheDocument();
  });

  it('accounting:write habilita criar/editar, mas NÃO contabilizar/estornar', () => {
    permState.me = me(['accounting:read', 'accounting:write']);
    renderWithProviders(<ContabilLancamentosPage />);
    expect(btn('Novo lançamento')).toBeInTheDocument();
    expect(btn('Editar')).toBeInTheDocument();
    expect(btn('Contabilizar')).not.toBeInTheDocument();
    expect(btn('Estornar')).not.toBeInTheDocument();
  });

  it('accounting:post habilita contabilizar e estornar', () => {
    permState.me = me(['accounting:read', 'accounting:write', 'accounting:post']);
    renderWithProviders(<ContabilLancamentosPage />);
    expect(btn('Contabilizar')).toBeInTheDocument();
    expect(btn('Estornar')).toBeInTheDocument();
  });
});
