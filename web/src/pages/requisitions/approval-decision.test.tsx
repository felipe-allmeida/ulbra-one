import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
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
  it('mostra o card só com dados da requisição (sem Fornecedor), painéis e as 3 ações — sem botão de adequação', () => {
    renderPage();
    // Unified card: short title + description as a field; Classificação/Financeiro present.
    expect(screen.getByRole('heading', { name: 'Requisição #42' })).toBeInTheDocument();
    expect(screen.getByText('Cadeiras novas')).toBeInTheDocument();
    expect(screen.getByText('Classificação')).toBeInTheDocument();
    expect(screen.getByText('Decisão orçamentária')).toBeInTheDocument();
    expect(screen.getByText('Histórico')).toBeInTheDocument();
    // The Approver decides without the Vendor block (showVendor=false).
    expect(screen.queryByText('Fornecedor')).not.toBeInTheDocument();
    expect(screen.queryByText('Papelaria Central')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Aprovar/ })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Devolver' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Devolver p/ ajustar financeiro' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Recusar/ })).toBeInTheDocument();
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

  it('Recusar exige motivo: confirmar só habilita após digitar e envia o reason', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Recusar/ }));
    const dialog = await screen.findByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'Recusar' });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByPlaceholderText('Motivo da recusa'), { target: { value: 'duplicada' } });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    expect(mockCancel).toHaveBeenCalledWith({ id: 'req_1', reason: 'duplicada' }, expect.anything());
  });

  it('Devolver não exige motivo (opcional)', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Devolver' }));
    const dialog = await screen.findByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'Devolver' });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    expect(mockReturn).toHaveBeenCalledWith({ id: 'req_1', reason: undefined }, expect.anything());
  });

  it('Devolver p/ ajustar financeiro devolve para o complemento financeiro', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Devolver p/ ajustar financeiro' }));
    const dialog = await screen.findByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'Devolver' });
    fireEvent.change(within(dialog).getByPlaceholderText('O que precisa ser ajustado?'), {
      target: { value: 'trocar vencimento' },
    });
    fireEvent.click(confirm);
    expect(mockReturnFinancial).toHaveBeenCalledWith({ id: 'req_1', reason: 'trocar vencimento' }, expect.anything());
  });

  it('fora de Approval é read-only: sem ações de decisão', () => {
    mockUseRequisition.mockReturnValue({ data: { ...REQ, status: 'Paid' }, isLoading: false });
    renderPage();
    expect(screen.queryByRole('button', { name: /Aprovar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Recusar/ })).not.toBeInTheDocument();
  });
});
