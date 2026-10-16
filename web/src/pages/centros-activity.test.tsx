import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n'; // pt-BR labels.
import { type CostCenter } from '../api';

const navigate = vi.fn();
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigate };
});

// Payables accordion gate: requires payables:read (Requester reaches the screen but lacks it).
const { permState } = vi.hoisted(() => ({ permState: { canPayables: true } }));
vi.mock('../permissions', async (orig) => ({
  ...(await orig<typeof import('../permissions')>()),
  usePermissions: () => ({
    can: (p: string) => (p === 'payables:read' ? permState.canPayables : false),
    canAny: () => false,
    me: null,
    isAdmin: false,
  }),
}));

const { mockRequisitions, mockPayables, mockSummary } = vi.hoisted(() => ({
  mockRequisitions: vi.fn(),
  mockPayables: vi.fn(),
  mockSummary: vi.fn(),
}));
vi.mock('../queries', () => ({
  useRequisitions: mockRequisitions,
  usePayables: mockPayables,
  useCostCenterBudgetSummary: mockSummary,
}));

import { CostCenterActivity } from './centros-activity';

const centro = { id: 'cc_1', companyId: 'company_1', name: 'EAD' } as unknown as CostCenter;

const aReq = {
  id: 'req_1',
  code: 42,
  vendorName: 'ACME Ltda',
  type: 'Purchase',
  estimateType: 'Full',
  startCompetency: '2026-08',
  endCompetency: null,
  estimateValue: 12000,
  status: 'Approval', // in-pipeline: no Payable yet, so it shows under "Requisições em andamento"
};

const aPayable = {
  payableId: 'pay_1',
  installmentId: 'inst_1',
  sequence: 1,
  installmentCount: 3,
  counterpartyName: 'ACME Ltda',
  documentNumber: 'NF-9',
  dueDate: '2026-08-20',
  amount: 4000,
  payableStatus: 'Scheduled',
};

beforeEach(() => {
  vi.clearAllMocks();
  permState.canPayables = true;
  mockSummary.mockReturnValue({
    data: [
      {
        costCenterId: 'cc_1',
        costCenterName: 'EAD',
        orcado: 40000,
        realized: 20000,
        committed: 5000,
        forecast: 3000,
        consumed: 25000,
        available: 15000,
        budgeted: true,
      },
    ],
  });
  mockRequisitions.mockReturnValue({ data: { items: [aReq], total: 1, totalPages: 1 }, isLoading: false });
  mockPayables.mockReturnValue({ data: { items: [aPayable], total: 1, totalPages: 1 }, isLoading: false });
});

function renderActivity() {
  return render(
    <MemoryRouter>
      <CostCenterActivity centro={centro} />
    </MemoryRouter>,
  );
}

describe('CostCenterActivity', () => {
  it('resumo da competência mostra orçado, realizado (gasto), empenhado e disponível', () => {
    renderActivity();
    expect(screen.getByText('Orçado')).toBeInTheDocument();
    expect(screen.getByText('Realizado (gasto)')).toBeInTheDocument();
    expect(screen.getByText('Empenhado')).toBeInTheDocument();
    expect(screen.getByText('Disponível')).toBeInTheDocument();
    // realized 20000 of budget 40000 = 50%
    expect(screen.getByText(/50% do orçado realizado/)).toBeInTheDocument();
    expect(screen.getByText(/^Competência \d{2}\/\d{4}/)).toBeInTheDocument();
  });

  it('accordion de requisições lista o item, repassa costCenterId + competência e filtra para em-andamento (dedup vs payable)', () => {
    renderActivity();
    expect(screen.getByText('#42')).toBeInTheDocument();
    expect(screen.getAllByText('ACME Ltda').length).toBeGreaterThan(0);
    expect(mockRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({
        companyId: 'company_1',
        costCenterId: 'cc_1',
        competency: expect.any(String),
        page: 1,
        // dedup: pipeline stages only (post-approval reqs are shown as their payable, not duplicated).
        statuses: expect.arrayContaining(['Triage', 'Approval']),
      }),
      expect.anything(),
    );
    expect(mockRequisitions.mock.calls[0][0].statuses).not.toContain('Paid');
  });

  it('accordion de contas a pagar lista a parcela e navega pelo payableId', () => {
    renderActivity();
    expect(screen.getByText('Contas a pagar')).toBeInTheDocument();
    expect(screen.getByText('1/3')).toBeInTheDocument();
    expect(screen.getByText(/NF-9/)).toBeInTheDocument();
    expect(mockPayables).toHaveBeenCalledWith(
      expect.objectContaining({ costCenterId: 'cc_1', competency: expect.any(String) }),
      expect.anything(),
    );
  });

  it('sem payables:read: oculta o accordion de contas a pagar e nem consulta o endpoint', () => {
    permState.canPayables = false;
    renderActivity();
    expect(screen.queryByText('Contas a pagar')).not.toBeInTheDocument();
    expect(mockPayables).not.toHaveBeenCalled();
    // requisitions stay visible (read is allowed for anyone who reaches the screen)
    expect(screen.getByText('Requisições em andamento')).toBeInTheDocument();
  });

  it('estado vazio quando não há requisições em andamento na competência', () => {
    mockRequisitions.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 }, isLoading: false });
    renderActivity();
    expect(screen.getByText('Nenhuma requisição em andamento nesta competência.')).toBeInTheDocument();
  });

  it('mostra o Pager e pagina quando há mais de uma página', () => {
    mockPayables.mockReturnValue({ data: { items: [aPayable], total: 25, totalPages: 3 }, isLoading: false });
    renderActivity();
    const next = screen.getByRole('button', { name: 'Próxima' });
    fireEvent.click(next);
    expect(mockPayables).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }), expect.anything());
  });

  it('trocar a competência refaz a consulta com o novo valor', () => {
    renderActivity();
    fireEvent.change(screen.getByLabelText('Competência'), { target: { value: '2026-03' } });
    expect(mockRequisitions).toHaveBeenLastCalledWith(
      expect.objectContaining({ competency: '2026-03' }),
      expect.anything(),
    );
  });
});

/**
 * A célula do badge de status parava a propagação sem pôr nada no lugar — a coluna inteira virava
 * zona morta numa linha que, no resto da largura, abre o detalhe. Mesmo padrão corrigido no #240.
 */
describe('CostCenterActivity — célula de status', () => {
  beforeEach(() => navigate.mockClear());

  it('abre a requisição quando o clique cai no badge de status', () => {
    renderActivity();

    fireEvent.click(screen.getByText('Aprovação'));

    expect(navigate).toHaveBeenCalledWith('/app/requisitions/req_1');
  });

  it('abre o título quando o clique cai no badge de status da parcela', () => {
    renderActivity();

    fireEvent.click(screen.getByText('Agendada'));

    expect(navigate).toHaveBeenCalledWith('/app/payables/pay_1');
  });
});
