import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseRequisitions, mockUseMyDecisions, mockNavigate, mockUseApp } = vi.hoisted(() => ({
  mockUseRequisitions: vi.fn(),
  mockUseMyDecisions: vi.fn(),
  mockNavigate: vi.fn(),
  mockUseApp: vi.fn(),
}));

vi.mock('../../store', () => ({ useApp: mockUseApp }));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../queries', () => ({
  useRequisitions: mockUseRequisitions,
  useMyRequisitionDecisions: mockUseMyDecisions,
  useUnits: () => ({ data: [] }),
  useProjects: () => ({ data: [] }),
  useVendor: () => ({ data: undefined }),
  useRequisitionBudgetControl: () => ({ data: undefined }),
  // hooks of the shared aside (ApproverAside)
  useCostCenterBudgetSummary: () => ({ data: [] }),
  useQueueBudgetControls: () => [],
  useQueueBudgetProjection: () => ({ data: undefined }),
}));
vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => mockNavigate,
}));

import { RequisitionApprovalInbox } from './approval-inbox';

const decision = (over: Partial<Record<string, unknown>> = {}) => ({
  requisitionId: 'req_1',
  code: 42,
  vendorId: 'vendor_1',
  vendorName: 'Papelaria Central',
  description: 'Cadeiras novas',
  estimateValue: 900,
  status: 'InPayment',
  decisionAction: 'Approve',
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
  mockUseMyDecisions.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
});

describe('RequisitionApprovalInbox', () => {
  it('lists the Complement status in the "All" tab (a returned requisition stays visible)', () => {
    render(
      <MemoryRouter>
        <RequisitionApprovalInbox />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByText('Todas'));

    // The "All" tab groups the in-flight statuses into one multi-status query — Complement must be in it,
    // otherwise a requisition sent back for complement would be invisible across every tab of the inbox.
    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ statuses: expect.arrayContaining(['Complement']), mine: false }),
      expect.anything(),
    );
    // AwaitingFiscalDocument goes into "Concluídas" (before it was invisible in the inbox).
    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ statuses: expect.arrayContaining(['AwaitingFiscalDocument']), mine: false }),
      expect.anything(),
    );
  });

  it('aba "Todas": seção paginada mostra o Pager e navega (linhas não são capadas)', () => {
    // A seção "em andamento" tem 84 itens em 5 páginas; a página traz 1 linha — o Pager precisa aparecer.
    mockUseRequisitions.mockImplementation((params: { status?: string; statuses?: string[] }) => {
      const set = params.statuses ?? (params.status ? [params.status] : []);
      if (set.includes('Triage'))
        return {
          data: {
            items: [
              { id: 'req_x', code: 7, description: 'Cadeiras', type: 'Purchase', status: 'Triage', estimateValue: 10 },
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
        <RequisitionApprovalInbox />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Todas'));

    expect(screen.getByText('Página 1 de 5')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }));
    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ statuses: expect.arrayContaining(['Triage']), page: 2 }),
      expect.anything(),
    );
  });

  it('"Histórico" agrupa minhas decisões por mês e mapeia as flags das 3 ações', () => {
    mockUseMyDecisions.mockReturnValue({
      data: {
        items: [
          decision({ decidedAt: '2026-07-12T10:00:00Z', decisionAction: 'Approve' }),
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
        <RequisitionApprovalInbox />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByText('Histórico'));

    expect(mockUseMyDecisions).toHaveBeenCalledWith('company_1', 1);
    expect(screen.getByText('Julho de 2026')).toBeInTheDocument();
    expect(screen.getByText('Junho de 2026')).toBeInTheDocument();
    expect(screen.getByText('Aprovado')).toBeInTheDocument();
    expect(screen.getByText('Devolvido')).toBeInTheDocument();
    expect(screen.getByText('Recusado')).toBeInTheDocument();
  });

  it('linha do histórico navega para a decisão read-only', () => {
    mockUseMyDecisions.mockReturnValue({
      data: { items: [decision()], total: 1, totalPages: 1 },
    });
    render(
      <MemoryRouter>
        <RequisitionApprovalInbox />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Histórico'));
    fireEvent.click(screen.getByText('Papelaria Central'));
    expect(mockNavigate).toHaveBeenCalledWith('/app/requisitions/approval/req_1', {
      state: { from: '/app/requisitions/approval' },
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
        <RequisitionApprovalInbox />
      </MemoryRouter>,
    );
    // Past the gate: the inbox tabs render, and the queue is queried with the consolidated sentinel.
    expect(screen.getByText('Histórico')).toBeInTheDocument();
    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: '__all__', status: 'Approval', mine: false }),
      expect.anything(),
    );
  });
});
