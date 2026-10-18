import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { setViewportMatches } from '../test/setup';
import '../i18n';
import { CashFlowPage } from './cash-flow';
import type { CashFlowResponse } from '../api';
import * as queries from '../queries';

// The page reads the selected company from the app store and the cash-flow data
// from TanStack Query hooks. Mock both so the test renders deterministically off
// fixed data — same pattern as the registry-forms page test (mock the data layer,
// render with providers, assert on translated text the app actually shows).
vi.mock('../store', () => ({
  useApp: () => ({
    empresas: [{ id: 'c1', legalName: 'ULBRA' }],
    empresaId: 'c1',
    empresa: { id: 'c1', legalName: 'ULBRA' },
    setEmpresaId: vi.fn(),
    refreshEmpresas: vi.fn(),
  }),
}));

vi.mock('../queries', async (orig) => {
  const actual = await orig<typeof import('../queries')>();
  return {
    ...actual,
    useCashFlow: vi.fn(),
    useCashFlowItems: vi.fn(),
    useSetReconciledBalance: vi.fn(),
  };
});

const useCashFlow = vi.mocked(queries.useCashFlow);
const useCashFlowItems = vi.mocked(queries.useCashFlowItems);
const useSetReconciledBalance = vi.mocked(queries.useSetReconciledBalance);

// Minimal query-result stub: only the fields the page reads (data, isLoading).
// Cast through unknown so the same factory serves both query hooks (different data types).
const asQuery = <R,>(over: { data?: unknown; isLoading?: boolean }): R =>
  ({ data: undefined, isLoading: false, ...over }) as unknown as R;

const report: CashFlowResponse = {
  asOf: '2026-06-19',
  granularity: 'Month',
  currency: 'BRL',
  startingBalance: 100000,
  accounts: [{ bankAccountId: 'ba1', alias: 'Conta Corrente', currentBalance: 100000, reconciledAt: '2026-06-01' }],
  kpis: {
    startingBalance: 100000,
    lowestProjectedBalance: 25000,
    lowestProjectedPeriodLabel: 'set/2026',
    runwayPeriods: null,
    netResult: -15000,
    totalInflowForecast: 60000,
    totalOutflowForecast: 75000,
  },
  overdueReceivable: 5000,
  periods: [
    {
      start: '2026-06-01',
      end: '2026-06-30',
      label: 'jun/2026',
      // isPast + non-null actuals so "Realizado até hoje" (the relocated fold, Task 17b) has content
      // to assert on — see 'CashFlowPage no telefone' below.
      isPast: true,
      openingBalance: 100000,
      closingBalance: 85000,
      inflowForecast: 20000,
      outflowForecast: 35000,
      netForecast: -15000,
      inflowActual: 5000,
      outflowActual: 12000,
      belowReserve: false,
    },
    {
      start: '2026-07-01',
      end: '2026-07-31',
      label: 'jul/2026',
      isPast: false,
      openingBalance: 85000,
      closingBalance: 70000,
      inflowForecast: 40000,
      outflowForecast: 55000,
      netForecast: -15000,
      inflowActual: null,
      outflowActual: null,
      belowReserve: false,
    },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  // Drill + mutation are not exercised in these render assertions; supply inert defaults.
  useCashFlowItems.mockReturnValue(asQuery<ReturnType<typeof queries.useCashFlowItems>>({ data: [] }));
  useSetReconciledBalance.mockReturnValue({
    mutate: vi.fn(),
    isPending: false,
    error: null,
  } as unknown as ReturnType<typeof queries.useSetReconciledBalance>);
});

describe('CashFlowPage', () => {
  it('renders the KPI cards and at least one period row from mocked data', () => {
    useCashFlow.mockReturnValue(asQuery<ReturnType<typeof queries.useCashFlow>>({ data: report }));
    renderWithProviders(<CashFlowPage />);

    // KPI cards (translated labels) render.
    expect(screen.getByText('Saldo hoje')).toBeInTheDocument();
    expect(screen.getByText('Menor saldo projetado')).toBeInTheDocument();
    expect(screen.getByText('Fôlego de caixa')).toBeInTheDocument();
    // runwayPeriods=null => "never breaches" copy.
    expect(screen.getByText('Não rompe no horizonte')).toBeInTheDocument();

    // Currency formatting is applied (brl, cent-exact) — the starting balance
    // appears as the KPI value and the first period's opening balance.
    expect(screen.getAllByText('R$ 100.000,00').length).toBeGreaterThan(0);

    // The overdue-receivable alert renders because overdueReceivable > 0.
    expect(screen.getByText('A receber em atraso')).toBeInTheDocument();

    // At least one period row renders in the periods table. The label appears in
    // several panels (chart axis, bars, table), so just assert it's present.
    expect(screen.getAllByText('jun/2026').length).toBeGreaterThan(0);
    expect(screen.getAllByText('jul/2026').length).toBeGreaterThan(0);
    // The periods table itself has a row per period (2 rows + header).
    const periodsTable = screen.getByText('Períodos').closest('.panel')!.querySelector('table tbody');
    expect(periodsTable!.querySelectorAll('tr')).toHaveLength(2);

    // The account-balances panel lists the seeded account.
    expect(screen.getByText('Conta Corrente')).toBeInTheDocument();
  });

  it('renders the empty state when there is no data', () => {
    useCashFlow.mockReturnValue(asQuery<ReturnType<typeof queries.useCashFlow>>({ data: undefined }));
    renderWithProviders(<CashFlowPage />);

    expect(screen.getByText('Sem dados de fluxo de caixa')).toBeInTheDocument();
    // KPI cards must NOT render in the empty state.
    expect(screen.queryByText('Saldo hoje')).not.toBeInTheDocument();
  });
});

describe('CashFlowPage desktop', () => {
  it('CashFlowPeriodsTable: mantém as 6 colunas/headers originais (Task 17b não vaza no desktop)', () => {
    useCashFlow.mockReturnValue(asQuery<ReturnType<typeof queries.useCashFlow>>({ data: report }));
    renderWithProviders(<CashFlowPage />);

    const periodsTable = screen.getByText('Períodos').closest('.panel')!.querySelector('table');
    const headers = [...periodsTable!.querySelectorAll('thead th')].map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Período', 'Saldo inicial', 'Entradas', 'Saídas', 'Resultado', 'Saldo final']);
  });
});

describe('CashFlowPage no telefone', () => {
  beforeEach(() => setViewportMatches(true));

  it('rotula "Realizado até hoje" em coluna própria, não emendado sob "Resultado" (Task 17b)', () => {
    useCashFlow.mockReturnValue(asQuery<ReturnType<typeof queries.useCashFlow>>({ data: report }));
    renderWithProviders(<CashFlowPage />);

    // jun/2026 é o período isPast com actuals, e o primeiro item do array `periods` — seu card é o
    // primeiro dentro do painel "Períodos" (escopar ao painel evita colidir com o texto do mesmo
    // label nos eixos do gráfico/barras, que também renderizam "jun/2026").
    const periodsPanel = screen.getByText('Períodos').closest('.panel')!;
    const card = periodsPanel.querySelector('.rt-card')!;
    fireEvent.click(card.querySelector('.rt-toggle')!);

    // "Realizado até hoje" tem `.rt-k` próprio, com o texto da linha ao lado.
    const realizedLabel = screen.getByText('Realizado até hoje', { selector: '.rt-k' });
    expect(realizedLabel.nextElementSibling).toHaveTextContent('Realizado até hoje: entradas R$ 5.000,00');
    expect(realizedLabel.nextElementSibling).toHaveTextContent('saídas R$ 12.000,00');

    // "Resultado" (o `net` que emendava o fold antes da correção) mostra só o valor do resultado —
    // não o texto de "Realizado até hoje" dobrado dentro dele.
    const resultLabel = screen.getByText('Resultado', { selector: '.rt-k' });
    expect(resultLabel.nextElementSibling).not.toHaveTextContent('Realizado até hoje');
  });
});
