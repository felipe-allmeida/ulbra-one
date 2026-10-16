import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseRequisitions, mockApproveTriage } = vi.hoisted(() => ({
  mockUseRequisitions: vi.fn(),
  mockApproveTriage: vi.fn(),
}));

vi.mock('../../queries', () => ({
  useRequisitions: mockUseRequisitions,
  useMe: () => ({
    data: { userId: 'user_me', isAdmin: false, permissions: ['requisitions:read', 'requisitions:triage'] },
  }),
  useVendor: () => ({ data: { id: 'vendor_1', name: 'Papelaria Central' } }),
  useUnits: () => ({ data: [{ id: 'unit_1', name: 'Canoas', acronym: 'CAN' }] }),
  useProjects: () => ({ data: [] }),
  useProjectMetrics: () => ({ data: undefined }),
  useRequisitionBudgetControl: () => ({
    data: {
      months: [
        {
          competency: '2026-07',
          orcado: 1000,
          realized: 0,
          committed: 0,
          forecast: 0,
          consumed: 100,
          available: 900,
          budgeted: true,
          cost: 300,
        },
      ],
      breaches: [],
      unbudgeted: [],
      candidateExceeds: false,
    },
    isLoading: false,
  }),
  useApproveTriage: () => ({ mutate: mockApproveTriage, isPending: false }),
  useReturnRequisitionForComplement: () => ({ mutate: vi.fn(), isPending: false }),
  useCancelRequisition: () => ({ mutate: vi.fn(), isPending: false }),
}));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

import { TriageSequence } from './triage-sequence';

const req = (id: string, code: number) => ({
  id,
  code,
  companyId: 'company_1',
  unitId: 'unit_1',
  costCenterId: 'cc_1',
  projectId: null,
  vendorId: 'vendor_1',
  type: 'Purchase',
  description: `Pedido ${code}`,
  observation: null,
  estimateValue: 300,
  estimateType: 'Full',
  startCompetency: '2026-07',
  endCompetency: null,
  paymentFrequency: null,
  paymentInstallmentsCount: null,
  firstDueDate: '2026-07-10',
  issueDate: null,
  totalCost: 300,
  realizedValue: 0,
  status: 'Triage',
  attachments: [],
  installments: [],
  transitions: [],
  createdAt: '2026-07-01T09:00:00Z',
  createdBy: 'user-aaa',
  statusChangedAt: '2026-07-01T09:00:00Z',
});

beforeEach(() => {
  vi.clearAllMocks();
  // onSuccess now reads the updated requisition's status to pick the toast (front-loaded → Approval).
  mockApproveTriage.mockImplementation((_vars: unknown, opts?: { onSuccess?: (data: unknown) => void }) =>
    opts?.onSuccess?.({ status: 'Approval' }),
  );
  mockUseRequisitions.mockReturnValue({
    data: { items: [req('req_1', 1), req('req_2', 2)], total: 2, totalPages: 1 },
  });
});

function renderSeq(onClose = vi.fn()) {
  render(
    <MemoryRouter>
      <TriageSequence companyId='company_1' onClose={onClose} />
    </MemoryRouter>,
  );
  return onClose;
}

describe('TriageSequence', () => {
  it('percorre a fila: 1 de 2 → Liberar avança → Pular fecha com o resumo decidiu/pulou', () => {
    renderSeq();
    expect(screen.getByText('1 de 2')).toBeInTheDocument();
    expect(screen.getByText('Papelaria Central')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Liberar para aprovação/ }));
    expect(mockApproveTriage).toHaveBeenCalledWith({ id: 'req_1' }, expect.anything());
    expect(screen.getByText('2 de 2')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Pular/ }));
    expect(screen.getByText('Tudo revisado')).toBeInTheDocument();
    expect(screen.getByText(/decidiu 1 e pulou 1/)).toBeInTheDocument();
  });

  it('snapshot congelado: a fila encolher por trás não re-embaralha o N de M', () => {
    renderSeq();
    expect(screen.getByText('1 de 2')).toBeInTheDocument();
    mockUseRequisitions.mockReturnValue({ data: { items: [req('req_2', 2)], total: 1, totalPages: 1 } });
    fireEvent.click(screen.getByRole('button', { name: /Liberar para aprovação/ }));
    expect(screen.getByText('2 de 2')).toBeInTheDocument();
  });

  it('"Voltar para a fila" e Esc fecham o overlay', () => {
    mockUseRequisitions.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
    const onClose = renderSeq();
    expect(screen.getByText('Tudo revisado')).toBeInTheDocument();
    expect(screen.getByText('Nada para revisar — a fila está vazia.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Voltar para a fila' }));
    expect(onClose).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(2);
  });
});
