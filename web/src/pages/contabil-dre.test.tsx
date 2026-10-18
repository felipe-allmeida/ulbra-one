import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n'; // initialize react-i18next so translated text renders (pt-BR).
import { ContabilDrePage } from './contabil-dre';
import type { IncomeStatement } from '../api';

// The empresa/consolidado scope comes from the topbar (store), not from a page filter.
vi.mock('../store', () => ({ useApp: () => ({ scopeCompanyId: undefined, scopeReady: true }) }));

// Mock the query hooks so the page renders synchronously from fixed data.
vi.mock('../queries', () => ({
  useCostCenters: () => ({ data: [] }),
  useUnits: () => ({ data: [] }),
  useIncomeStatement: () => mockIncome(),
  useMe: () => ({ data: null }), // no permissions → the year-end-closing action stays hidden
  useYearEndClosing: () => ({ mutate: vi.fn(), isPending: false }),
}));

let mockIncome: () => { data: IncomeStatement | undefined; isLoading: boolean };

const dre: IncomeStatement = {
  companyId: null,
  costCenterId: null,
  from: '2026-01-01',
  to: '2026-06-30',
  sections: [
    {
      line: 'GrossRevenue',
      total: 1000,
      lines: [
        {
          ledgerAccountId: 'ledgacct_1',
          accountCode: '3.2.05.10.020',
          accountName: 'Receita de serviços',
          dreLine: 'GrossRevenue',
          amount: 1000,
        },
      ],
    },
    {
      line: 'OperatingExpenses',
      total: -600,
      lines: [
        {
          ledgerAccountId: 'ledgacct_2',
          accountCode: '4.2.05.05.020',
          accountName: 'Despesas com pessoal',
          dreLine: 'OperatingExpenses',
          amount: -600,
        },
      ],
    },
  ],
  grossRevenue: 1000,
  revenueDeductions: 0,
  netRevenue: 1000,
  costOfSales: 0,
  grossProfit: 1000,
  otherOperatingIncome: 0,
  operatingExpenses: -600,
  operatingResult: 400,
  financialRevenue: 0,
  financialExpense: 0,
  financialResult: 0,
  otherResult: 0,
  preTaxResult: 400,
  incomeTax: 0,
  socialContribution: 0,
  netIncome: 400,
};

describe('ContabilDrePage (DRE / income statement)', () => {
  beforeEach(() => {
    mockIncome = () => ({ data: dre, isLoading: false });
  });

  it('renders the cascade sections, account lines and running subtotals', () => {
    renderWithProviders(<ContabilDrePage />);

    // Account lines within their DRE-line sections.
    expect(screen.getByText('Receita de serviços')).toBeInTheDocument();
    expect(screen.getByText('Despesas com pessoal')).toBeInTheDocument();

    // Cascade subtotals are labelled.
    expect(screen.getByText('Receita Operacional Líquida')).toBeInTheDocument();
    expect(screen.getByText('Lucro Bruto')).toBeInTheDocument();
    expect(screen.getByText('Resultado Operacional')).toBeInTheDocument();
    expect(screen.getByText('Lucro/Prejuízo Líquido do Exercício')).toBeInTheDocument();

    // Net income rendered in BRL (R$ 400,00).
    expect(screen.getAllByText(/400,00/).length).toBeGreaterThan(0);
  });

  it('shows the empty state when there are no sections', () => {
    mockIncome = () => ({ data: { ...dre, sections: [] }, isLoading: false });
    renderWithProviders(<ContabilDrePage />);
    expect(screen.getByText('Sem resultado')).toBeInTheDocument();
  });
});
