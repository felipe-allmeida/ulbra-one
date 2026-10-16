import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseMe, mockUseRequisitions, mockUseStageCounts } = vi.hoisted(() => ({
  mockUseMe: vi.fn(),
  mockUseRequisitions: vi.fn(),
  mockUseStageCounts: vi.fn(),
}));

vi.mock('../../store', () => ({
  ALL_COMPANIES: '__all__',
  useApp: () => ({
    empresas: [{ id: 'company_1', legalName: 'ACME' }],
    empresa: { id: 'company_1', legalName: 'ACME' },
    empresaId: 'company_1',
    isAllCompanies: false,
    scopeReady: true,
  }),
}));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('../../queries', () => ({
  useMe: mockUseMe,
  useRequisitions: mockUseRequisitions,
  useRequisitionStageCounts: mockUseStageCounts,
  useUnits: () => ({ data: [] }),
  useCostCenters: () => ({ data: [] }),
  useApproveTriage: () => ({ mutate: vi.fn(), isPending: false }),
  useApproveRequisition: () => ({ mutate: vi.fn(), isPending: false }),
}));

import { RequisitionsLayout } from './layout';

beforeEach(() => {
  mockUseStageCounts.mockReturnValue({ counts: { Triage: 1, Approval: 2 }, isLoading: false });
  mockUseRequisitions.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
  mockUseMe.mockReturnValue({ data: { isAdmin: false, permissions: ['requisitions:read'] } });
});

describe('RequisitionsLayout', () => {
  it('renderiza as abas de estágio', () => {
    render(
      <MemoryRouter initialEntries={['/app/requisitions']}>
        <RequisitionsLayout />
      </MemoryRouter>,
    );
    expect(screen.getAllByText('Rascunhos').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Triagem').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Aprovação').length).toBeGreaterThan(0);
    expect(mockUseRequisitions).toHaveBeenCalled();
  });

  it('esconde "Nova requisição" sem permissão de escrita', () => {
    render(
      <MemoryRouter initialEntries={['/app/requisitions']}>
        <RequisitionsLayout />
      </MemoryRouter>,
    );
    expect(screen.queryByText('Nova requisição')).not.toBeInTheDocument();
  });

  it('mostra "Nova requisição" para admin', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: true, permissions: [] } });
    render(
      <MemoryRouter initialEntries={['/app/requisitions']}>
        <RequisitionsLayout />
      </MemoryRouter>,
    );
    expect(screen.getByText('Nova requisição')).toBeInTheDocument();
  });
});
