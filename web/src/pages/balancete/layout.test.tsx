import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // initialize react-i18next so translated text renders (pt-BR).
import { BalancetePage } from './layout';
import type { TrialBalance } from '../../api';

// The empresa/consolidado scope comes from the topbar (store), not from a page filter.
vi.mock('../../store', () => ({ useApp: () => ({ scopeCompanyId: undefined, scopeReady: true }) }));

// Mock the query hooks so the page renders synchronously from fixed data.
vi.mock('../../queries', () => ({
  useCostCenters: () => ({ data: [] }),
  useUnits: () => ({ data: [] }),
  useTrialBalance: () => mockTb(),
}));

let mockTb: () => { data: TrialBalance | undefined; isLoading: boolean };

const tb: TrialBalance = {
  companyId: null,
  from: '2026-02-01',
  to: '2026-02-28',
  costCenterId: null,
  rows: [
    {
      ledgerAccountId: 'ledgacct_1',
      accountCode: '1.1',
      reducedCode: '11',
      accountName: 'Caixa',
      accountGroup: 'Asset',
      normalBalance: 'Debit',
      openingBalance: 500,
      totalDebit: 200,
      totalCredit: 50,
      balance: 650,
    },
  ],
  totalDebit: 200,
  totalCredit: 50,
  isBalanced: true,
};

describe('BalancetePage (balancete / trial balance)', () => {
  beforeEach(() => {
    mockTb = () => ({ data: tb, isLoading: false });
  });

  it('renders the period columns (opening / debit / credit / closing) and the balancing badge', () => {
    renderWithProviders(
      <MemoryRouter>
        <BalancetePage />
      </MemoryRouter>,
    );

    expect(screen.getByText('Saldo anterior')).toBeInTheDocument();
    expect(screen.getByText('Código reduzido')).toBeInTheDocument();
    expect(screen.getByText('11')).toBeInTheDocument(); // reduced code cell
    expect(screen.getByText('Caixa')).toBeInTheDocument();
    expect(screen.getByText('Exportar CSV')).toBeInTheDocument(); // CSV export button
    expect(screen.getByText(/500,00/)).toBeInTheDocument(); // opening
    expect(screen.getByText(/650,00/)).toBeInTheDocument(); // closing
    expect(screen.getByText('Débitos = Créditos')).toBeInTheDocument();
  });

  it('replaces the balancing badge with a neutral label when filtered by cost center', () => {
    // A cost-center slice filters by line, so it legitimately does not balance — the double-entry badge
    // must not claim "unbalanced" there.
    mockTb = () => ({
      data: { ...tb, costCenterId: 'cost_center_x', totalDebit: 200, totalCredit: 0, isBalanced: false },
      isLoading: false,
    });
    renderWithProviders(
      <MemoryRouter>
        <BalancetePage />
      </MemoryRouter>,
    );
    expect(screen.getByText('Parcial — filtrado por natureza orçamentária')).toBeInTheDocument();
    expect(screen.queryByText('Débitos ≠ Créditos')).not.toBeInTheDocument();
    expect(screen.queryByText('Débitos = Créditos')).not.toBeInTheDocument();
  });

  it('shows the empty state when there are no rows', () => {
    mockTb = () => ({
      data: { ...tb, rows: [], totalDebit: 0, totalCredit: 0 },
      isLoading: false,
    });
    renderWithProviders(
      <MemoryRouter>
        <BalancetePage />
      </MemoryRouter>,
    );
    expect(screen.getByText('Sem movimento')).toBeInTheDocument();
  });
});
