import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseRequisitions, mockUseMyTriageDecisions, mockNavigate, mockUseApp } = vi.hoisted(() => ({
  mockUseRequisitions: vi.fn(),
  mockUseMyTriageDecisions: vi.fn(),
  mockNavigate: vi.fn(),
  mockUseApp: vi.fn(),
}));

vi.mock('../../store', () => ({ useApp: mockUseApp }));
vi.mock('../../queries', () => ({
  useRequisitions: mockUseRequisitions,
  useMyRequisitionTriageDecisions: mockUseMyTriageDecisions,
  useUnits: () => ({ data: [] }),
  useProjects: () => ({ data: [] }),
  useVendor: () => ({ data: undefined }),
  useRequisitionBudgetControl: () => ({ data: undefined }),
  // hooks do aside (ControllershipAside)
  useCostCenterBudgetSummary: () => ({ data: [] }),
  useQueueBudgetControls: () => [],
  useQueueBudgetProjection: () => ({ data: undefined }),
}));
vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => mockNavigate,
}));

import { RequisitionTriageInbox } from './triage-inbox';

const decision = (over: Partial<Record<string, unknown>> = {}) => ({
  requisitionId: 'req_1',
  code: 42,
  vendorId: 'vendor_1',
  vendorName: 'Papelaria Central',
  description: 'Cadeiras novas',
  estimateValue: 900,
  status: 'FinancialComplement',
  decisionAction: 'ApproveTriage',
  decisionReason: null,
  decidedAt: '2026-07-12T10:00:00Z',
  ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  mockUseApp.mockReturnValue({
    empresa: { id: 'company_1', legalName: 'ACME' },
    empresaId: 'company_1',
    empresas: [{ id: 'company_1', legalName: 'ACME' }],
    isAllCompanies: false,
    scopeReady: true,
  });
  mockUseRequisitions.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
  mockUseMyTriageDecisions.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
});

describe('RequisitionTriageInbox', () => {
  it('aba "Todas": seção paginada mostra o Pager e navega (linhas não são capadas)', () => {
    // "em andamento" (inProgress) inclui Approval; 84 itens em 5 páginas, 1 linha por página → Pager aparece.
    mockUseRequisitions.mockImplementation((params: { status?: string; statuses?: string[] }) => {
      const set = params.statuses ?? (params.status ? [params.status] : []);
      if (set.includes('Approval'))
        return {
          data: {
            items: [
              {
                id: 'req_x',
                code: 7,
                description: 'Cadeiras',
                type: 'Purchase',
                status: 'Approval',
                estimateValue: 10,
              },
            ],
            total: 84,
            totalPages: 5,
            page: 1,
          },
        };
      return { data: { items: [], total: 0, totalPages: 1 } };
    });

    render(
      <MemoryRouter>
        <RequisitionTriageInbox />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Todas'));

    expect(screen.getByText('Página 1 de 5')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }));
    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ statuses: expect.arrayContaining(['Approval']), page: 2 }),
      expect.anything(),
    );
  });

  it('"Todas" agrupa os status por seção em uma query multi-status (Complement + AwaitingFiscalDocument visíveis)', () => {
    render(
      <MemoryRouter>
        <RequisitionTriageInbox />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByText('Todas'));

    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ statuses: expect.arrayContaining(['Complement']), mine: false }),
      expect.anything(),
    );
    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ statuses: expect.arrayContaining(['AwaitingFiscalDocument']), mine: false }),
      expect.anything(),
    );
  });

  it('"Todas" coloca Triage na seção "Aguardando você" (perspectiva da Controladoria)', () => {
    mockUseRequisitions.mockImplementation((params: { status?: string }) => {
      if (params.status === 'Triage') {
        return {
          data: {
            items: [
              {
                id: 'req_9',
                code: 9,
                description: 'Cadeiras',
                type: 'Purchase',
                unitId: 'u1',
                status: 'Triage',
                estimateValue: 500,
              },
            ],
            total: 1,
            totalPages: 1,
          },
        };
      }
      return { data: { items: [], total: 0, totalPages: 1 } };
    });
    render(
      <MemoryRouter>
        <RequisitionTriageInbox />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Todas'));
    expect(screen.getByText('Aguardando você')).toBeInTheDocument();
    expect(screen.getByText('#9 — Cadeiras')).toBeInTheDocument();
  });

  it('"Histórico" agrupa minhas decisões de triagem por mês e mapeia as flags', () => {
    mockUseMyTriageDecisions.mockReturnValue({
      data: {
        items: [
          decision({ decidedAt: '2026-07-12T10:00:00Z', decisionAction: 'ApproveTriage' }),
          decision({
            requisitionId: 'req_2',
            decidedAt: '2026-07-09T10:00:00Z',
            decisionAction: 'ReturnForComplement',
          }),
          decision({ requisitionId: 'req_3', decidedAt: '2026-06-14T10:00:00Z', decisionAction: 'Cancel' }),
        ],
        total: 3,
        totalPages: 1,
      },
    });
    render(
      <MemoryRouter>
        <RequisitionTriageInbox />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByText('Histórico'));

    expect(mockUseMyTriageDecisions).toHaveBeenCalledWith('company_1', 1);
    expect(screen.getByText('Julho de 2026')).toBeInTheDocument();
    expect(screen.getByText('Junho de 2026')).toBeInTheDocument();
    expect(screen.getByText('Liberado')).toBeInTheDocument();
    expect(screen.getByText('Devolvido')).toBeInTheDocument();
    expect(screen.getByText('Recusado')).toBeInTheDocument();
  });

  it('linha do histórico navega para a decisão de triagem', () => {
    mockUseMyTriageDecisions.mockReturnValue({
      data: { items: [decision()], total: 1, totalPages: 1 },
    });
    render(
      <MemoryRouter>
        <RequisitionTriageInbox />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Histórico'));
    fireEvent.click(screen.getByText('Papelaria Central'));
    expect(mockNavigate).toHaveBeenCalledWith('/app/requisitions/triage/req_1', {
      state: { from: '/app/requisitions/triage' },
    });
  });

  // AEL-906: consolidated "Todas" (empresa === null) must list content, not the pick-a-company gate.
  it('lists across all companies in the consolidated "Todas" view', () => {
    mockUseApp.mockReturnValue({
      empresa: null,
      empresaId: '__all__',
      empresas: [{ id: 'company_1', legalName: 'ACME' }],
      isAllCompanies: true,
      scopeReady: true,
    });
    render(
      <MemoryRouter>
        <RequisitionTriageInbox />
      </MemoryRouter>,
    );
    expect(screen.getByText('Histórico')).toBeInTheDocument();
    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: '__all__', status: 'Triage', mine: false }),
      expect.anything(),
    );
  });
});
