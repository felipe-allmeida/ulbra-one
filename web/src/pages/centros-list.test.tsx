import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';

// The CC card shows realized ÷ budget usage for roles with requisitions:read; without it, it falls
// back to the budget-share mode.
const { permState } = vi.hoisted(() => ({ permState: { canReq: true } }));
vi.mock('../permissions', async (orig) => ({
  ...(await orig<typeof import('../permissions')>()),
  usePermissions: () => ({
    can: (p: string) => (p === 'requisitions:read' ? permState.canReq : p === 'core:write'),
    canAny: () => false,
    me: null,
    isAdmin: false,
  }),
}));

vi.mock('../store', () => ({
  ALL_COMPANIES: '__all__',
  useApp: () => ({
    empresas: [{ id: 'company_1', legalName: 'ACME' }],
    empresa: { id: 'company_1', legalName: 'ACME' },
    empresaId: 'company_1',
    isAllCompanies: false,
    scopeReady: true,
  }),
}));

const { mockCostCenters, mockUnits, mockRealized } = vi.hoisted(() => ({
  mockCostCenters: vi.fn(),
  mockUnits: vi.fn(),
  mockRealized: vi.fn(),
}));
vi.mock('../queries', () => ({
  useCostCenters: mockCostCenters,
  useUnits: mockUnits,
  useCostCenterRealized: mockRealized,
  useCostCenter: () => ({ data: null }),
}));

import { CentrosListPage } from './centros';

beforeEach(() => {
  vi.clearAllMocks();
  permState.canReq = true;
  mockUnits.mockReturnValue({ data: [{ id: 'unit_1', acronym: 'CAN' }] });
  mockCostCenters.mockReturnValue({
    data: [
      { id: 'cc_1', code: 'ACAD', name: 'Acadêmico', annualBudget: 100000, autoApprovalLimit: 0, unitIds: ['unit_1'] },
      { id: 'cc_2', code: 'MKT', name: 'Marketing', annualBudget: 50000, autoApprovalLimit: 0, unitIds: ['unit_1'] },
    ],
  });
  mockRealized.mockReturnValue({ data: [{ costCenterId: 'cc_1', realized: 26700 }] });
});

function renderPage() {
  return render(
    <MemoryRouter>
      <CentrosListPage />
    </MemoryRouter>,
  );
}

describe('CentrosListPage — card mostra executado', () => {
  it('com requisitions:read: card mostra disponível + gasto (espelha budgets, não participação)', () => {
    renderPage();
    // Available is the main figure (cc-orc), spend is the line under the bar (cc-meta) — one per card.
    expect(screen.getAllByText('disponível')).toHaveLength(2);
    expect(screen.getAllByText('gasto')).toHaveLength(2);
    expect(screen.getAllByText(/orçamento\/ano/)).toHaveLength(2); // annual budget kept as the sub-line
    expect(mockRealized).toHaveBeenCalledWith('company_1', { enabled: true });
  });

  it('sem requisitions:read: cai para participação no orçamento (fatia do total, sem gasto/disponível)', () => {
    permState.canReq = false;
    renderPage();
    expect(screen.getAllByText('participação no orçamento')).toHaveLength(2);
    expect(screen.getByText('67%')).toBeInTheDocument(); // 100.000 / 150.000
    expect(screen.queryByText('gasto')).not.toBeInTheDocument();
    expect(screen.queryByText('disponível')).not.toBeInTheDocument();
  });
});
