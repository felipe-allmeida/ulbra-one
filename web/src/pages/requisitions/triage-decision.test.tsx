import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import '../../i18n';

const { mockUseRequisition, mockUseMe, mockUseBudgetControl, mockApproveTriage, mockReturn, mockCancel, mockNavigate } =
  vi.hoisted(() => ({
    mockUseRequisition: vi.fn(),
    mockUseMe: vi.fn(),
    mockUseBudgetControl: vi.fn(),
    mockApproveTriage: vi.fn(),
    mockReturn: vi.fn(),
    mockCancel: vi.fn(),
    mockNavigate: vi.fn(),
  }));

vi.mock('../../queries', () => ({
  useRequisition: mockUseRequisition,
  useMe: mockUseMe,
  useVendor: () => ({
    data: { id: 'vendor_1', name: 'Papelaria Central', document: '11.111.111/0001-11', taxRegime: 'Unknown' },
  }),
  useWithholdingPreview: () => ({ data: [] }),
  useUnits: () => ({ data: [{ id: 'unit_1', name: 'Canoas', acronym: 'CAN' }] }),
  useCostCenters: () => ({ data: [{ id: 'cc_1', name: 'Material de consumo' }] }),
  useProjects: () => ({ data: [] }),
  useProjectMetrics: () => ({ data: undefined }),
  useRequisitionBudgetControl: mockUseBudgetControl,
  // Hooks do aside compartilhado (ControllershipAside).
  useRequisitions: () => ({ data: { items: [], total: 0 } }),
  useQueueBudgetControls: () => [],
  useCostCenterBudgetSummary: () => ({ data: [] }),
  useUploadRequisitionAttachment: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateRequisitionAttachmentKind: () => ({ mutate: vi.fn(), isPending: false }),
  useApproveTriage: () => ({ mutate: mockApproveTriage, isPending: false }),
  useReturnRequisitionForComplement: () => ({ mutate: mockReturn, isPending: false }),
  useCancelRequisition: () => ({ mutate: mockCancel, isPending: false }),
}));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => mockNavigate,
}));

import { RequisitionTriageDecisionPage } from './triage-decision';

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
  status: 'Triage',
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
    <MemoryRouter initialEntries={['/app/requisitions/triage/req_1']}>
      <Routes>
        <Route path='/app/requisitions/triage/:id' element={<RequisitionTriageDecisionPage />} />
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
      permissions: [
        'requisitions:read',
        'requisitions:triage',
        'core:read',
        'registry:read',
        'fiscal:read',
        'projects:read',
      ],
    },
  });
  mockUseBudgetControl.mockReturnValue(CONTROL_OK);
});

describe('RequisitionTriageDecisionPage', () => {
  it('mostra o card unificado (título, bloco Fornecedor), decisão orçamentária e as ações — sem adequação quando cabe', () => {
    renderPage();
    // Card unificado: título curto + descrição como campo próprio; o bloco Fornecedor aparece na triagem (registry:read).
    expect(screen.getByRole('heading', { name: 'Requisição #42' })).toBeInTheDocument();
    expect(screen.getByText('Cadeiras novas')).toBeInTheDocument();
    expect(screen.getByText('Fornecedor')).toBeInTheDocument();
    expect(screen.getByText('Papelaria Central')).toBeInTheDocument();
    expect(screen.getByText('Decisão orçamentária')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Liberar para aprovação/ })).toBeEnabled();
    expect(screen.getByRole('button', { name: /Devolver/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Recusar/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Solicitar adequação/ })).not.toBeInTheDocument();
  });

  it('estouro NÃO bloqueia a triagem: Liberar continua habilitado + aviso + botão Solicitar adequação', () => {
    mockUseBudgetControl.mockReturnValue({
      data: {
        ...CONTROL_OK.data,
        breaches: [{ competency: '2026-07', orcado: 1000, available: 100, cost: 900 }],
        candidateExceeds: true,
      },
      isLoading: false,
    });
    renderPage();
    // The key divergence vs. final approval: here the primary button does NOT disable on an overage.
    expect(screen.getByRole('button', { name: /Liberar para aprovação/ })).toBeEnabled();
    expect(screen.getByText(/Solicite uma Adequação Orçamentária/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Solicitar adequação/ })).toBeInTheDocument();
  });

  it('Solicitar adequação navega para o form com pré-preenchimento (CC/unidade/competência/valor)', () => {
    mockUseBudgetControl.mockReturnValue({
      data: {
        ...CONTROL_OK.data,
        breaches: [{ competency: '2026-07', orcado: 1000, available: 100, cost: 900 }],
        candidateExceeds: true,
      },
      isLoading: false,
    });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Solicitar adequação/ }));
    const target = mockNavigate.mock.calls
      .map((c) => c[0])
      .find((u) => typeof u === 'string' && u.startsWith('/app/budget-adjustments/new'));
    expect(target).toBeTruthy();
    expect(target).toContain('costCenterId=cc_1');
    expect(target).toContain('unitId=unit_1');
    expect(target).toContain('competency=2026-07');
    expect(target).toContain('value=800');
  });

  it('re-triagem (ReturnToTriageForRevalue) mostra o callout explicativo', () => {
    mockUseRequisition.mockReturnValue({
      data: {
        ...REQ,
        transitions: [
          ...REQ.transitions,
          {
            action: 'ReturnToTriageForRevalue',
            fromStatus: 'FinancialComplement',
            toStatus: 'Triage',
            actorId: 'user_bbb',
            occurredAt: '2026-07-05T10:00:00Z',
            reason: null,
          },
        ],
      },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByText('Voltou para triagem')).toBeInTheDocument();
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

  it('fora de Triage é read-only: sem ações de decisão', () => {
    mockUseRequisition.mockReturnValue({ data: { ...REQ, status: 'Approval' }, isLoading: false });
    renderPage();
    expect(screen.queryByRole('button', { name: /Liberar para aprovação/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Recusar/ })).not.toBeInTheDocument();
  });
});
