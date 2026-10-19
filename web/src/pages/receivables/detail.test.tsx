import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

const { mockUseReceivable, mockUseAudit } = vi.hoisted(() => ({ mockUseReceivable: vi.fn(), mockUseAudit: vi.fn() }));
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    useParams: () => ({ id: 'rcv_1' }),
    // BackLink reads location.state for origin-aware back; renderWithProviders has no Router.
    useLocation: () => ({ pathname: '/app/receivables/rcv_1', search: '', hash: '', state: null, key: 'default' }),
  };
});
vi.mock('../../queries', () => ({
  useReceivable: mockUseReceivable,
  useReceivableAuditTrail: mockUseAudit,
  useCancelReceivable: () => ({ mutate: vi.fn() }),
  useCostCenters: () => ({ data: [] }),
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
}));

import { ReceivableDetailPage } from './detail';

beforeEach(() => {
  mockUseReceivable.mockReturnValue({
    data: {
      id: 'rcv_1',
      companyId: 'company_1',
      payerName: 'Cliente A',
      description: 'Mensalidade',
      status: 'AwaitingReceipt',
      totalAmount: 500,
      costCenterId: 'cc_1',
      documentNumber: 'NF-1',
      issueDate: '2026-06-01',
      installments: [
        {
          id: 'i1',
          sequence: 1,
          dueDate: '2026-06-30',
          amount: 500,
          status: 'Open',
          receivedAmount: 0,
          receipts: [],
          isOverdue: false,
        },
      ],
    },
  });
  mockUseAudit.mockReturnValue({
    data: [{ type: 'Created', actorId: 'u1', actorName: 'Ana', timestamp: '2026-06-01T10:00:00Z', metadata: {} }],
    isLoading: false,
  });
});

describe('ReceivableDetailPage', () => {
  it('renders the receivable header and the audit timeline', () => {
    renderWithProviders(<ReceivableDetailPage />);
    expect(screen.getAllByText(/Cliente A/).length).toBeGreaterThan(0);
    expect(screen.getByText('Título criado')).toBeInTheDocument();
  });
});
