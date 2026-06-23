import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n'; // initialize react-i18next so translated text renders (pt-BR).
import { ContabilDrePage } from './contabil-dre';
import type { IncomeStatement } from '../api';

// Mock the query hooks so the page renders synchronously from fixed data.
vi.mock('../queries', () => ({
  useCompanies: () => ({ data: [] }),
  useIncomeStatement: () => mockIncome(),
}));

let mockIncome: () => { data: IncomeStatement | undefined; isLoading: boolean };

const dre: IncomeStatement = {
  companyId: null,
  from: '2026-01-01',
  to: '2026-06-30',
  revenues: [
    {
      ledgerAccountId: 'ledgacct_1',
      accountCode: '3.1',
      accountName: 'Receita de serviços',
      accountGroup: 'Revenue',
      amount: 1000,
    },
  ],
  expenses: [
    {
      ledgerAccountId: 'ledgacct_2',
      accountCode: '4.1',
      accountName: 'Despesas com pessoal',
      accountGroup: 'Expense',
      amount: 600,
    },
  ],
  totalRevenue: 1000,
  totalExpense: 600,
  result: 400,
};

describe('ContabilDrePage (DRE / income statement)', () => {
  beforeEach(() => {
    mockIncome = () => ({ data: dre, isLoading: false });
  });

  it('renders the income statement sections, lines and the period result', () => {
    renderWithProviders(<ContabilDrePage />);

    expect(screen.getByText('Receita de serviços')).toBeInTheDocument();
    expect(screen.getByText('Despesas com pessoal')).toBeInTheDocument();
    // A positive result is labelled as profit (Lucro do exercício).
    expect(screen.getByText('Lucro do exercício')).toBeInTheDocument();
    // Result amount rendered with cents in BRL (R$ 400,00).
    expect(screen.getByText(/400,00/)).toBeInTheDocument();
  });

  it('shows the empty state when there are no revenues or expenses', () => {
    mockIncome = () => ({
      data: { ...dre, revenues: [], expenses: [], totalRevenue: 0, totalExpense: 0, result: 0 },
      isLoading: false,
    });
    renderWithProviders(<ContabilDrePage />);
    expect(screen.getByText('Sem resultado')).toBeInTheDocument();
  });
});
