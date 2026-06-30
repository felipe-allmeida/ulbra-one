import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

const { mockUsePayable, mockUseAudit } = vi.hoisted(() => ({ mockUsePayable: vi.fn(), mockUseAudit: vi.fn() }));
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn(), useParams: () => ({ id: 'payable_1' }) };
});
vi.mock('../../queries', () => ({
  usePayable: mockUsePayable,
  usePayableAuditTrail: mockUseAudit,
  useApprovePayable: () => ({ mutate: vi.fn() }),
  useRejectPayable: () => ({ mutate: vi.fn() }),
  useCancelPayable: () => ({ mutate: vi.fn() }),
  useDeletePayableAttachment: () => ({ mutate: vi.fn() }),
  useCostCenters: () => ({ data: [] }),
  useExpenseCategories: () => ({ data: [] }),
  useChartAccounts: () => ({ data: [] }),
}));

import { PayableDetailPage } from './detail';

beforeEach(() => {
  mockUsePayable.mockReturnValue({
    data: {
      id: 'payable_1',
      companyId: 'company_1',
      counterpartyName: 'ACME',
      description: 'NF 1',
      status: 'Scheduled',
      totalAmount: 1000,
      costCenterId: 'cc_1',
      categoryId: null,
      accountingAccountId: null,
      financialAccountId: null,
      documentNumber: 'NF-1',
      issueDate: '2026-06-01',
      installments: [
        { id: 'i1', sequence: 1, dueDate: '2026-06-30', amount: 1000, status: 'Open', paidAmount: 0, payments: [] },
      ],
      attachments: [],
    },
  });
  mockUseAudit.mockReturnValue({
    data: [
      { type: 'Created', actorId: 'u1', actorName: 'João', timestamp: '2026-06-01T10:00:00Z', metadata: {} },
      { type: 'Approved', actorId: 'u2', actorName: 'Maria', timestamp: '2026-06-02T10:00:00Z', metadata: {} },
    ],
    isLoading: false,
  });
});

describe('PayableDetailPage', () => {
  it('renders the payable header and the audit timeline events', () => {
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByText(/ACME/)).toBeInTheDocument();
    expect(screen.getByText('Título criado')).toBeInTheDocument();
    expect(screen.getByText('Aprovado')).toBeInTheDocument();
  });

  it('shows the resolved approver name, not the raw user id, in the approved-by label', () => {
    mockUsePayable.mockReturnValue({
      data: {
        id: 'payable_1',
        companyId: 'company_1',
        counterpartyName: 'ACME',
        description: 'NF 1',
        status: 'Scheduled',
        totalAmount: 1000,
        costCenterId: 'cc_1',
        categoryId: null,
        accountingAccountId: null,
        financialAccountId: null,
        documentNumber: 'NF-1',
        issueDate: '2026-06-01',
        approvedBy: '3fa85f64-5714-4219-9730-000000000000',
        approvedByName: 'Carlos Silva',
        approvedAt: '2026-06-02T10:00:00Z',
        installments: [],
        attachments: [],
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByText(/Aprovado por Carlos Silva/)).toBeInTheDocument();
    expect(screen.queryByText(/3fa85f64/)).not.toBeInTheDocument();
  });
});
