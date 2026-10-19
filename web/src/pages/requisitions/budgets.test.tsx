import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseBudgetSummary, mockUseProjectMetricsList } = vi.hoisted(() => ({
  mockUseBudgetSummary: vi.fn(),
  mockUseProjectMetricsList: vi.fn(),
}));

vi.mock('../../store', () => ({
  useApp: () => ({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' }),
}));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../queries', () => ({
  useMe: () => ({ data: { userId: 'u', isAdmin: false, permissions: ['requisitions:approve'] } }),
  useCostCenterBudgetSummary: mockUseBudgetSummary,
  useUnits: () => ({ data: [{ id: 'unit_1', name: 'Canoas', acronym: 'CAN' }] }),
  useProjects: () => ({ data: [{ id: 'proj_1', name: 'Reforma Bloco C', unitId: 'unit_1' }] }),
  useProjectMetricsList: mockUseProjectMetricsList,
  // hooks of the shared aside (ApproverAside)
  useRequisitions: () => ({ data: { items: [], total: 0, totalPages: 1 } }),
  useQueueBudgetControls: () => [],
}));

import { RequisitionBudgetsPage } from './budgets';

beforeEach(() => {
  vi.clearAllMocks();
  mockUseBudgetSummary.mockReturnValue({
    data: [
      {
        costCenterId: 'cc_1',
        costCenterName: 'Material de consumo',
        orcado: 40000,
        realized: 20000,
        committed: 5000,
        forecast: 0,
        consumed: 25000,
        available: 15000,
        budgeted: true,
      },
    ],
  });
  // The batched list hook returns one entry per input id (aligned to the projects list).
  mockUseProjectMetricsList.mockReturnValue([
    {
      data: {
        id: 'proj_1',
        name: 'Reforma Bloco C',
        budgetLimit: 120000,
        realizado: 85000,
        previsto: 127500,
        saldo: 35000,
        percentRealizado: 71,
        percentPrevisto: 106,
        isOverCeiling: true,
      },
    },
  ]);
});

function renderPage() {
  render(
    <MemoryRouter>
      <RequisitionBudgetsPage />
    </MemoryRouter>,
  );
}

describe('RequisitionBudgetsPage', () => {
  it('mostra o total da unidade e a linha por natureza; expandir revela a grade + barra', () => {
    renderPage();
    expect(screen.getByText('Total da unidade')).toBeInTheDocument();
    expect(screen.getByText(/Orçado R\$ 40\.000,00 · consumido R\$ 25\.000,00/)).toBeInTheDocument();

    const row = screen.getByRole('button', { name: /Material de consumo/ });
    expect(row).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(row);
    expect(row).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('Uso')).toBeInTheDocument();
    expect(screen.getByText('Pago')).toBeInTheDocument();
  });

  it('seletor de unidade repassa o unitId ao budget-summary', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'unit_1' } });
    expect(mockUseBudgetSummary).toHaveBeenLastCalledWith('company_1', expect.any(String), 'unit_1');
  });

  it('projeto acima do teto: flag + nota com o valor do estouro', () => {
    renderPage();
    const row = screen.getByRole('button', { name: /Reforma Bloco C/ });
    expect(screen.getByText('estoura o teto')).toBeInTheDocument();
    fireEvent.click(row);
    expect(screen.getByText(/Estoura o teto por R\$ 7\.500/)).toBeInTheDocument();
  });
});
