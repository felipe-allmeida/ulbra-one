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
  useLedgerAccounts: () => ({ data: [] }),
  useSetPayableWithholdings: () => ({ mutate: vi.fn(), isPending: false }),
  // BalanceForecastBanner (rendered on PendingApproval) uses this; loading short-circuits it harmlessly.
  useCashFlow: () => ({ isLoading: true }),
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
      documentNumber: 'NF-1',
      issueDate: '2026-06-01',
      installments: [
        { id: 'i1', sequence: 1, dueDate: '2026-06-30', amount: 1000, netAmount: 1000, status: 'Open', paidAmount: 0, payments: [] },
      ],
      attachments: [],
      withholdings: [],
      totalWithheld: 0,
      netTotal: 1000,
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
        documentNumber: 'NF-1',
        issueDate: '2026-06-01',
        approvedBy: '3fa85f64-5714-4219-9730-000000000000',
        approvedByName: 'Carlos Silva',
        approvedAt: '2026-06-02T10:00:00Z',
        installments: [],
        attachments: [],
        withholdings: [],
        totalWithheld: 0,
        netTotal: 1000,
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByText(/Aprovado por Carlos Silva/)).toBeInTheDocument();
    expect(screen.queryByText(/3fa85f64/)).not.toBeInTheDocument();
  });

  it('shows the "Valor líquido" card and the per-installment net column once accounting completes', () => {
    mockUsePayable.mockReturnValue({
      data: {
        id: 'payable_1', companyId: 'company_1', counterpartyName: 'ACME', description: 'NF 1',
        status: 'Scheduled', totalAmount: 1000, costCenterId: 'cc_1', categoryId: null,
        accountingAccountId: null, documentNumber: 'NF-1', issueDate: '2026-06-01',
        installments: [
          { id: 'i1', sequence: 1, dueDate: '2026-06-30', amount: 1000, netAmount: 940, status: 'Open', paidAmount: 0, payments: [] },
        ],
        attachments: [], withholdings: [{ id: 'w1', taxType: 'Irrf', baseAmount: 1000, rate: null, amount: 60 }],
        totalWithheld: 60, netTotal: 940,
      },
    });
    renderWithProviders(<PayableDetailPage />);
    // Card next to "Valor total" (the label also exists inside the read-only WithholdingSection).
    expect(screen.getAllByText('Valor líquido').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText(/R\$\s?940/).length).toBeGreaterThanOrEqual(2); // card + installment net cell
    expect(screen.getByText('Valor bruto')).toBeInTheDocument(); // installments header switches
  });

  it('hides the net card and net column while the withholdings are not locked (PendingApproval)', () => {
    mockUsePayable.mockReturnValue({
      data: {
        id: 'payable_1', companyId: 'company_1', counterpartyName: 'ACME', description: 'NF 1',
        status: 'PendingApproval', totalAmount: 1000, costCenterId: 'cc_1', categoryId: null,
        accountingAccountId: null, documentNumber: 'NF-1', issueDate: '2026-06-01',
        installments: [
          { id: 'i1', sequence: 1, dueDate: '2026-06-30', amount: 1000, netAmount: 1000, status: 'Open', paidAmount: 0, payments: [] },
        ],
        attachments: [], withholdings: [], totalWithheld: 0, netTotal: 1000,
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.queryByText('Valor bruto')).toBeNull(); // installments keep the single amount column
  });

  it('keeps retentions read-only on the approval detail (editor hidden)', () => {
    mockUsePayable.mockReturnValue({
      data: {
        id: 'payable_1', companyId: 'company_1', counterpartyName: 'ACME', description: 'NF 1',
        status: 'PendingApproval', totalAmount: 1000, costCenterId: 'cc_1', categoryId: null,
        accountingAccountId: null, documentNumber: 'NF-1', issueDate: '2026-06-01',
        installments: [], attachments: [], withholdings: [], totalWithheld: 0, netTotal: 1000,
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.queryByText('Adicionar retenção')).not.toBeInTheDocument();
  });

  it('enables the retentions editor in the accounting stage (PendingAccounting)', () => {
    mockUsePayable.mockReturnValue({
      data: {
        id: 'payable_1', companyId: 'company_1', counterpartyName: 'ACME', description: 'NF 1',
        status: 'PendingAccounting', totalAmount: 1000, costCenterId: 'cc_1', categoryId: null,
        accountingAccountId: null, documentNumber: 'NF-1', issueDate: '2026-06-01',
        installments: [], attachments: [], withholdings: [], totalWithheld: 0, netTotal: 1000,
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByText('Adicionar retenção')).toBeInTheDocument();
  });
});
