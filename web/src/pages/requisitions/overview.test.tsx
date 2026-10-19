import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseBudgetSummary, mockUseProjectMetricsList, mockNavigate } = vi.hoisted(() => ({
  mockUseBudgetSummary: vi.fn(),
  mockUseProjectMetricsList: vi.fn(),
  mockNavigate: vi.fn(),
}));

vi.mock('../../store', () => ({
  useApp: () => ({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' }),
}));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../queries', () => ({
  useMe: () => ({ data: { userId: 'u', isAdmin: false, permissions: ['requisitions:approve'] } }),
  useCostCenterBudgetSummary: mockUseBudgetSummary,
  useRequisitions: () => ({ data: { items: [], total: 3, totalPages: 1 } }),
  useUnits: () => ({ data: [{ id: 'unit_1', name: 'Canoas', acronym: 'CAN' }] }),
  useProjects: () => ({ data: [{ id: 'proj_1', name: 'Reforma Bloco C' }] }),
  useProjectMetricsList: mockUseProjectMetricsList,
  useQueueBudgetControls: () => [],
}));
vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => mockNavigate,
}));

import { RequisitionOverviewPage } from './overview';

const line = (over: Partial<Record<string, unknown>> = {}) => ({
  costCenterId: 'cc_1',
  costCenterName: 'Material de consumo',
  orcado: 1000,
  realized: 400,
  committed: 100,
  forecast: 0,
  consumed: 500,
  available: 500,
  budgeted: true,
  ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  mockUseBudgetSummary.mockReturnValue({ data: [line(), line({ costCenterId: 'cc_2', costCenterName: 'TI' })] });
  mockUseProjectMetricsList.mockReturnValue([{ data: undefined }]);
});

describe('RequisitionOverviewPage', () => {
  it('soma as linhas do budget-summary nos tiles e mostra a fila pendente', () => {
    render(
      <MemoryRouter>
        <RequisitionOverviewPage />
      </MemoryRouter>,
    );
    // Σ de 2 linhas iguais: orçado 2.000 · consumido 1.000 · disponível 1.000.
    expect(screen.getByText('R$ 2.000,00')).toBeInTheDocument();
    expect(screen.getAllByText('R$ 1.000,00').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Aguardando você')).toBeInTheDocument();
    // "3" aparece no tile da fila e no aside (Pendentes) — ambos da mesma query.
    expect(screen.getAllByText('3').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText(/Uso 50%/)).toBeInTheDocument();
  });

  it('Atenção: natureza estourada (bad) e uso ≥85% (warn)', () => {
    mockUseBudgetSummary.mockReturnValue({
      data: [
        line({ costCenterName: 'Manutenção', consumed: 1100, available: -100 }),
        line({ costCenterId: 'cc_2', costCenterName: 'TI', consumed: 900, available: 100 }),
      ],
    });
    render(
      <MemoryRouter>
        <RequisitionOverviewPage />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Manutenção estourou o orçamento do mês em R\$ 100,00/)).toBeInTheDocument();
    expect(screen.getByText(/TI já usou 90% do orçado/)).toBeInTheDocument();
  });

  it('Atenção: projeto acima do teto entra com o valor do estouro', () => {
    mockUseProjectMetricsList.mockReturnValue([
      { data: { id: 'proj_1', name: 'Reforma Bloco C', budgetLimit: 120000, previsto: 127500, isOverCeiling: true } },
    ]);
    render(
      <MemoryRouter>
        <RequisitionOverviewPage />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Reforma Bloco C estoura o teto em R\$ 7\.500/)).toBeInTheDocument();
  });

  it('Atenção: com projetos saudáveis e sem alertas mostra o estado tranquilo (não fica em branco)', () => {
    // Todas as naturezas ok (uso 50%) + projeto dentro do teto → a mensagem de "nada" precisa aparecer.
    mockUseProjectMetricsList.mockReturnValue([{ data: { id: 'proj_1', name: 'Reforma', isOverCeiling: false } }]);
    render(
      <MemoryRouter>
        <RequisitionOverviewPage />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Nada exigindo atenção/)).toBeInTheDocument();
  });

  it('"Ver mais" navega para a tela de Orçamentos', () => {
    render(
      <MemoryRouter>
        <RequisitionOverviewPage />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: /Orçamentos/ }));
    expect(mockNavigate).toHaveBeenCalledWith('/app/requisitions/budgets');
  });
});
