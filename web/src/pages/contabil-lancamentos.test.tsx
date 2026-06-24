import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n'; // initialize react-i18next so translated text renders (pt-BR).
import type { LedgerAccount } from '../api';

// Mock the data hooks so the page renders synchronously from fixed data and the
// mutations are no-ops. We only exercise the draft form's local balancing logic.
const accounts: LedgerAccount[] = [
  {
    id: 'a1',
    companyId: 'c1',
    code: '1.01.01',
    name: 'Caixa',
    accountGroup: 'Asset',
    normalBalance: 'Debit',
    kind: 'Analytical',
    parentId: null,
    isActive: true,
  },
  {
    id: 'a2',
    companyId: 'c1',
    code: '4.01.01',
    name: 'Receita de serviços',
    accountGroup: 'Revenue',
    normalBalance: 'Credit',
    kind: 'Analytical',
    parentId: null,
    isActive: true,
  },
];

vi.mock('../queries', () => {
  const noopMutation = () => ({ mutate: vi.fn(), isPending: false, error: null });
  return {
    useCreateJournalEntry: noopMutation,
    useUpdateJournalEntry: noopMutation,
    useJournalEntries: () => ({ data: [], isLoading: false }),
    useLedgerAccounts: () => ({ data: accounts, isLoading: false }),
    usePostJournalEntry: noopMutation,
    useReverseJournalEntry: noopMutation,
    useCostCenters: () => ({ data: [] }),
    useCompanies: () => ({ data: [{ id: 'c1', legalName: 'Empresa Teste' }] }),
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

import { ContabilLancamentosPage } from './contabil-lancamentos';

/** Opens the new-entry drawer and returns its two amount inputs (role=spinbutton). */
function openDraftForm() {
  renderWithProviders(<ContabilLancamentosPage />);
  fireEvent.click(screen.getByRole('button', { name: 'Novo lançamento' }));
  const amounts = screen.getAllByRole('spinbutton');
  expect(amounts).toHaveLength(2);
  return amounts;
}

describe('ContabilLancamentosPage (journal entry draft form)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('keeps the débito/crédito totals live as amounts are typed (regression: stale memo)', () => {
    const amounts = openDraftForm();

    // Before any input the totals are zero and the entry is "Não balanceado".
    const totals = () => document.querySelector('.journal-totals')!.textContent ?? '';
    expect(totals()).toContain('Não balanceado');

    // Type equal debit/credit amounts; the totals must update on every change —
    // `watch('lines')` returns a stable reference RHF mutates in place, so a memo
    // keyed on it would never recompute. The totals are computed each render.
    fireEvent.change(amounts[0], { target: { value: '1234.56' } });
    fireEvent.change(amounts[1], { target: { value: '1234.56' } });

    expect(totals()).toContain('1.234,56');
    expect(totals()).toContain('Balanceado');
    expect(totals()).not.toContain('Não balanceado');
  });

  it('enables Save only when balanced and disables it again when unbalanced', () => {
    const amounts = openDraftForm();
    const save = () => screen.getByRole('button', { name: /Salvar|Salvando/ });

    // Empty form: nothing to post -> disabled.
    expect(save()).toBeDisabled();

    // Balanced -> enabled.
    fireEvent.change(amounts[0], { target: { value: '50' } });
    fireEvent.change(amounts[1], { target: { value: '50' } });
    expect(save()).toBeEnabled();

    // Unbalance one side -> disabled again.
    fireEvent.change(amounts[1], { target: { value: '40' } });
    expect(save()).toBeDisabled();
  });
});
