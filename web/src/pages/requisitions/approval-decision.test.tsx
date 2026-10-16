import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest';
import { act, cleanup, render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { setViewportMatches } from '../../test/setup';
import '../../i18n';

const {
  mockUseRequisition,
  mockUseMe,
  mockUseBudgetControl,
  mockApprove,
  mockReturn,
  mockReturnFinancial,
  mockCancel,
} = vi.hoisted(() => ({
  mockUseRequisition: vi.fn(),
  mockUseMe: vi.fn(),
  mockUseBudgetControl: vi.fn(),
  mockApprove: vi.fn(),
  mockReturn: vi.fn(),
  mockReturnFinancial: vi.fn(),
  mockCancel: vi.fn(),
}));

vi.mock('../../queries', () => ({
  useRequisition: mockUseRequisition,
  useMe: mockUseMe,
  useVendor: () => ({ data: { id: 'vendor_1', name: 'Papelaria Central', document: '11.111.111/0001-11' } }),
  useUnits: () => ({ data: [{ id: 'unit_1', name: 'Canoas', acronym: 'CAN' }] }),
  useCostCenters: () => ({ data: [{ id: 'cc_1', name: 'Material de consumo' }] }),
  useProjects: () => ({ data: [] }),
  useProjectMetrics: () => ({ data: undefined }),
  useRequisitionBudgetControl: mockUseBudgetControl,
  // Hooks of the shared aside (ApproverAside).
  useRequisitions: () => ({ data: { items: [], total: 0 } }),
  useQueueBudgetControls: () => [],
  useCostCenterBudgetSummary: () => ({ data: [] }),
  useUploadRequisitionAttachment: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateRequisitionAttachmentKind: () => ({ mutate: vi.fn(), isPending: false }),
  useApproveRequisition: () => ({ mutate: mockApprove, isPending: false }),
  useReturnRequisitionForComplement: () => ({ mutate: mockReturn, isPending: false }),
  useReturnRequisitionToFinancialComplement: () => ({ mutate: mockReturnFinancial, isPending: false }),
  useCancelRequisition: () => ({ mutate: mockCancel, isPending: false }),
}));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

import { RequisitionApprovalDecisionPage } from './approval-decision';

const REQ = {
  id: 'req_1',
  code: 42,
  companyId: 'company_1',
  unitId: 'unit_1',
  costCenterId: 'cc_1',
  projectId: null,
  vendorId: 'vendor_1',
  type: 'Purchase',
  description: 'Cadeiras novas',
  observation: null,
  estimateValue: 900,
  estimateType: 'Full',
  startCompetency: '2026-07',
  endCompetency: null,
  paymentFrequency: null,
  paymentInstallmentsCount: null,
  firstDueDate: '2026-07-10',
  issueDate: null,
  totalCost: 900,
  realizedValue: 0,
  status: 'Approval',
  attachments: [],
  installments: [],
  transitions: [
    {
      action: 'Submit',
      fromStatus: 'Draft',
      toStatus: 'Triage',
      actorId: 'user_aaa',
      occurredAt: '2026-07-01T10:00:00Z',
      reason: null,
    },
  ],
  createdAt: '2026-07-01T09:00:00Z',
  createdBy: 'user-aaa',
  statusChangedAt: new Date().toISOString(),
};

const CONTROL_OK = {
  data: {
    months: [
      {
        competency: '2026-07',
        orcado: 1000,
        realized: 100,
        committed: 0,
        forecast: 0,
        consumed: 100,
        available: 900,
        budgeted: true,
        cost: 900,
      },
    ],
    breaches: [],
    unbudgeted: [],
    candidateExceeds: false,
  },
  isLoading: false,
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/app/requisitions/approval/req_1']}>
      <Routes>
        <Route path='/app/requisitions/approval/:id' element={<RequisitionApprovalDecisionPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseRequisition.mockReturnValue({ data: REQ, isLoading: false });
  mockUseMe.mockReturnValue({
    data: {
      userId: 'user_me',
      isAdmin: false,
      permissions: ['requisitions:read', 'requisitions:approve', 'core:read', 'registry:read', 'projects:read'],
    },
  });
  mockUseBudgetControl.mockReturnValue(CONTROL_OK);
});

describe('RequisitionApprovalDecisionPage', () => {
  it('mostra o card só com dados da solicitação (sem Fornecedor), painéis e as 3 ações — sem botão de adequação', () => {
    renderPage();
    // Unified card: short title + description as a field; Classificação/Financeiro present.
    expect(screen.getByRole('heading', { name: 'Solicitação #42' })).toBeInTheDocument();
    expect(screen.getByText('Cadeiras novas')).toBeInTheDocument();
    expect(screen.getByText('Classificação')).toBeInTheDocument();
    expect(screen.getByText('Decisão orçamentária')).toBeInTheDocument();
    expect(screen.getByText('Histórico')).toBeInTheDocument();
    // The Approver decides without the Vendor block (showVendor=false).
    expect(screen.queryByText('Fornecedor')).not.toBeInTheDocument();
    expect(screen.queryByText('Papelaria Central')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Aprovar/ })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Solicitar ajustes' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Solicitar ajustes nas parcelas' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Rejeitar' })).toBeInTheDocument();
    expect(screen.queryByText('Abrir adequação orçamentária')).not.toBeInTheDocument();
  });

  it('estouro: Aprovar desabilitado + aviso de Adequação, ainda sem botão morto', () => {
    mockUseBudgetControl.mockReturnValue({
      data: {
        ...CONTROL_OK.data,
        breaches: [{ competency: '2026-07', orcado: 1000, available: 100, cost: 900 }],
        candidateExceeds: true,
      },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByRole('button', { name: /Aprovar/ })).toBeDisabled();
    expect(screen.getByText(/Adequação Orçamentária \(Controladoria\)/)).toBeInTheDocument();
    expect(screen.queryByText('Abrir adequação orçamentária')).not.toBeInTheDocument();
  });

  it('Rejeitar exige motivo: confirmar só habilita após digitar e envia o reason', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Rejeitar' }));
    const dialog = await screen.findByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'Rejeitar solicitação' });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByPlaceholderText('Motivo da rejeição'), { target: { value: 'duplicada' } });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    expect(mockCancel).toHaveBeenCalledWith({ id: 'req_1', reason: 'duplicada' }, expect.anything());
  });

  // O motivo É o pedido de ajuste: confirmar sem ele devolveria a solicitação sem dizer o que corrigir.
  it('Solicitar ajuste exige motivo: confirmar só habilita depois de digitar', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar ajustes' }));
    const dialog = await screen.findByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'Solicitar ajustes' });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByPlaceholderText('O que precisa ser ajustado?'), {
      target: { value: 'falta a cotação' },
    });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    expect(mockReturn).toHaveBeenCalledWith({ id: 'req_1', reason: 'falta a cotação' }, expect.anything());
  });

  it('Solicitar ajuste nas parcelas devolve para o complemento financeiro', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar ajustes nas parcelas' }));
    const dialog = await screen.findByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'Solicitar ajustes' });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByPlaceholderText('O que precisa ser ajustado nas parcelas?'), {
      target: { value: 'trocar vencimento' },
    });
    fireEvent.click(confirm);
    expect(mockReturnFinancial).toHaveBeenCalledWith({ id: 'req_1', reason: 'trocar vencimento' }, expect.anything());
  });

  // O painel é sticky só onde o aside é sticky (>=1200px). Abaixo disso o aside desempilha para o fim da
  // página, então a decisão sobe para a coluna única — senão ficaria depois do Histórico, que é o problema
  // que este redesign existe para resolver.
  describe('colocação do painel de decisão', () => {
    // `setViewportMatches` dispara 'change' nos listeners; desmontar antes evita atualizar
    // um componente vivo fora de act().
    afterEach(() => {
      cleanup();
      act(() => setViewportMatches(false));
    });

    it('em telas largas o painel encabeça o aside sticky', () => {
      act(() => setViewportMatches(true));
      const { container } = renderPage();
      const aside = container.querySelector('.aside-layout-side');
      expect(aside).not.toBeNull();
      expect(aside?.querySelector('.spanel-decision')).not.toBeNull();
      expect(container.querySelector('.aside-layout-main .spanel-decision')).toBeNull();
    });

    it('abaixo de 1200px o painel vai para a coluna principal', () => {
      const { container } = renderPage();
      expect(container.querySelector('.aside-layout-main .spanel-decision')).not.toBeNull();
      expect(container.querySelector('.aside-layout-side .spanel-decision')).toBeNull();
    });
  });

  it('fora de Approval é read-only: sem ações de decisão', () => {
    mockUseRequisition.mockReturnValue({ data: { ...REQ, status: 'Paid' }, isLoading: false });
    renderPage();
    expect(screen.queryByRole('button', { name: /Aprovar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rejeitar' })).not.toBeInTheDocument();
  });
});
