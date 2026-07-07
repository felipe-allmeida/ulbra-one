/**
 * Tests for:
 * (a) PayableStatusBadge — new 7-state set renders the correct pt-BR label.
 * (b) RowClassify / RowClassifyWithPrefill — pre-selects suggested accounts
 *     (via Combobox value) when suggestedAccountingAccountId / suggestedFinancialAccountId
 *     are present on the fetched PayableResponse.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

// ---------- mock react-router-dom (not used by these components, but some imports pull it) ----------
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn(), useParams: () => ({}) };
});

// ---------- mock store ----------
vi.mock('../../store', async (orig) => {
  const actual = await orig<typeof import('../../store')>();
  const empresa = { id: 'company_1', legalName: 'ACME LTDA', cnpj: '00000000000191' };
  return {
    ...actual,
    useApp: () => ({
      empresa,
      empresaId: 'company_1',
      empresas: [empresa],
      setEmpresaId: vi.fn(),
    }),
  };
});

// ---------- mock queries ----------
// vi.hoisted ensures the mock refs are available when vi.mock factory runs (hoisted to top).
const { mockUsePayable, mockUsePayables } = vi.hoisted(() => ({
  mockUsePayable: vi.fn(),
  mockUsePayables: vi.fn(),
}));

vi.mock('../../queries', async (orig) => {
  const actual = await orig<typeof import('../../queries')>();

  const noopMut = () => ({
    mutate: vi.fn(),
    mutateAsync: vi.fn(),
    isPending: false,
    error: null,
    reset: vi.fn(),
    isSuccess: false,
    data: undefined,
  });

  return {
    ...actual,
    usePayable: mockUsePayable,
    usePayables: mockUsePayables,
    useChartAccounts: () => ({
      data: [
        {
          id: 'acc_1',
          code: '1.1.01',
          name: 'Despesas gerais',
          kind: 'Analytical',
          isActive: true,
          chartType: 'Accounting',
        },
      ],
      isLoading: false,
    }),
    useClassifyPayable: noopMut,
    useClassifyBatch: noopMut,
    useAiClassificationSuggestion: noopMut,
    useCostCenters: () => ({ data: [] }),
    useExpenseCategories: () => ({ data: [] }),
  };
});

// ---------- Imports must come AFTER vi.mock() calls ----------
import { PayableStatusBadge } from './badges';
import { ClassificacaoPage } from '../classificacao';
import type { PayableStatus } from '../../api';

// ============================================================
// (a) Badge label tests
// ============================================================
describe('PayableStatusBadge — new 7-state set', () => {
  const cases: [PayableStatus, string][] = [
    ['Received', 'Recebida'],
    ['PendingClassification', 'Aguardando classificação'],
    ['PendingApproval', 'Aguardando aprovação'],
    ['Scheduled', 'Agendada'],
    ['Paid', 'Paga'],
    ['Rejected', 'Rejeitada'],
    ['Cancelled', 'Cancelada'],
  ];

  it.each(cases)('status %s renders pt-BR label "%s"', (status, expectedLabel) => {
    render(<PayableStatusBadge s={status} />);
    expect(screen.getByText(expectedLabel)).toBeTruthy();
  });
});

// ============================================================
// (b) Classification page — pre-fill from AI suggestion fields
// ============================================================

/** Minimal PayableListItem shape as returned by usePayables */
const pendingClassificationItem = {
  payableId: 'payable_abc',
  installmentId: 'inst_abc',
  sequence: 1,
  installmentCount: 1,
  counterpartyName: 'Fornecedor Teste',
  description: 'Serviços de TI',
  costCenterId: 'cost_center_1',
  categoryId: null,
  dueDate: '2025-07-01',
  amount: 1000,
  installmentStatus: 'Open' as const,
  payableStatus: 'PendingClassification' as const,
};

/** Minimal PayableResponse with AI suggestion fields populated */
const payableWithSuggestion = {
  id: 'payable_abc',
  companyId: 'company_1',
  origin: 'Manual' as const,
  vendorId: 'vendor_1',
  counterpartyName: 'Fornecedor Teste',
  documentNumber: null,
  issueDate: null,
  description: 'Serviços de TI',
  costCenterId: 'cost_center_1',
  categoryId: null,
  totalAmount: 1000,
  status: 'PendingClassification' as const,
  rejectionReason: null,
  cancelReason: null,
  approvedBy: null,
  approvedByName: null,
  approvedAt: null,
  rejectedBy: null,
  rejectedByName: null,
  rejectedAt: null,
  accountingAccountId: null,
  isClassified: false,
  suggestedAccountingAccountId: 'acc_1',
  classificationConfidence: 0.75,
  classificationRationale: 'Baseado em histórico de lançamentos similares.',
  createdAt: '2025-06-01T00:00:00Z',
  installments: [],
  attachments: [],
};

describe('ClassificacaoPage — AI suggestion pre-fill', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Default: usePayable returns no data (no suggestions)
    mockUsePayable.mockReturnValue({ data: undefined, isLoading: false });
  });

  it('shows AI hint banner and rationale when suggestion fields are present', () => {
    // usePayables returns the pending classification row
    mockUsePayables.mockReturnValue({
      data: { items: [pendingClassificationItem], total: 1, totalPages: 1, page: 1, pageSize: 20 },
      isLoading: false,
    });

    // usePayable (called by RowClassifyWithPrefill) returns the full payable with suggestions
    mockUsePayable.mockReturnValue({ data: payableWithSuggestion, isLoading: false });

    renderWithProviders(<ClassificacaoPage />);

    // The AI hint banner should be visible
    expect(screen.getByText('Sugestão da IA — confirme')).toBeTruthy();

    // The confidence and rationale should be visible
    expect(screen.getByText(/75%/)).toBeTruthy();
    expect(screen.getByText('Baseado em histórico de lançamentos similares.')).toBeTruthy();

    // The Combobox trigger must show the pre-selected accounting account label (verifies the useEffect sync).
    // acc_1 → "1.1.01 — Despesas gerais" (accounting).
    const buttons = screen.getAllByRole('button');
    const buttonTexts = buttons.map((b) => b.textContent ?? '');
    expect(buttonTexts.some((t) => t.includes('1.1.01'))).toBe(true);
  });

  it('does not show the AI hint banner when no suggestion fields are present', () => {
    mockUsePayables.mockReturnValue({
      data: { items: [pendingClassificationItem], total: 1, totalPages: 1, page: 1, pageSize: 20 },
      isLoading: false,
    });

    // usePayable returns payable WITHOUT suggestion fields
    mockUsePayable.mockReturnValue({
      data: {
        ...payableWithSuggestion,
        suggestedAccountingAccountId: null,
        classificationConfidence: null,
        classificationRationale: null,
      },
      isLoading: false,
    });

    renderWithProviders(<ClassificacaoPage />);

    expect(screen.queryByText('Sugestão da IA — confirme')).toBeNull();
  });
});
