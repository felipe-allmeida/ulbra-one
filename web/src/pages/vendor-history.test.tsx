import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';
import { renderWithProviders } from '../test/renderWithProviders';
import { PersonType, type Vendor } from '../api';
import { VendorHistory } from './vendor-history';

const navigate = vi.fn();
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigate };
});

const useRequisitionsMock = vi.fn();
const usePayablesMock = vi.fn();

vi.mock('../queries', () => ({
  useRequisitions: (params: unknown, opts: unknown) => useRequisitionsMock(params, opts),
  usePayables: (params: unknown, opts: unknown) => usePayablesMock(params, opts),
}));

// The vendor detail is reachable by roles without requisitions/payables read; grant both so the tab mounts.
vi.mock('../permissions', async (orig) => ({
  ...(await orig<typeof import('../permissions')>()),
  usePermissions: () => ({ can: () => true, canAny: () => true, me: null, isAdmin: true }),
}));

// VendorHistory scopes by the selected company (vendors are global); provide one.
vi.mock('../store', () => ({ useApp: () => ({ empresaId: 'c1' }) }));

const vendor: Vendor = {
  id: 'v1',
  personType: PersonType.Company,
  name: 'ACME LTDA',
  document: '11222333000181',
} as Vendor;

function mockList(mock: ReturnType<typeof vi.fn>, items: unknown[]) {
  mock.mockReturnValue({ data: { items, total: items.length, totalPages: 1 }, isLoading: false });
}

describe('VendorHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockList(useRequisitionsMock, [
      {
        id: 'r1',
        code: 101,
        estimateValue: 500,
        status: 'Approval',
        type: 'Full',
        estimateType: 'Full',
        startCompetency: '2026-08',
        endCompetency: null,
      },
    ]);
    mockList(usePayablesMock, [
      {
        installmentId: 'i1',
        payableId: 'p1',
        dueDate: '2026-08-10',
        documentNumber: 'NF-1',
        sequence: 1,
        installmentCount: 1,
        amount: 500,
        payableStatus: 'Scheduled',
      },
    ]);
  });

  it('renders both accordions with the vendor rows', () => {
    renderWithProviders(
      <MemoryRouter>
        <VendorHistory vendor={vendor} />
      </MemoryRouter>,
    );
    expect(screen.getByText('Solicitações em andamento')).toBeInTheDocument();
    expect(screen.getByText('Contas a pagar')).toBeInTheDocument();
    expect(screen.getByText('#101')).toBeInTheDocument();
    expect(screen.getByText('NF-1')).toBeInTheDocument();
  });

  it('scopes requisitions to the vendor and hides post-approval stages (dedup)', () => {
    renderWithProviders(
      <MemoryRouter>
        <VendorHistory vendor={vendor} />
      </MemoryRouter>,
    );
    const params = useRequisitionsMock.mock.calls[0][0];
    expect(params.vendorId).toBe('v1');
    // Only in-pipeline stages — InPayment/Paid (which have a Payable) must be excluded to avoid duplication.
    expect(params.statuses).toEqual(['Draft', 'Triage', 'FinancialComplement', 'Approval', 'Complement']);
    expect(params.statuses).not.toContain('InPayment');
    expect(params.statuses).not.toContain('Paid');
  });

  it('scopes payables to the vendor with a due-date range', () => {
    renderWithProviders(
      <MemoryRouter>
        <VendorHistory vendor={vendor} />
      </MemoryRouter>,
    );
    const params = usePayablesMock.mock.calls[0][0];
    expect(params.vendorId).toBe('v1');
    expect(params.dueFrom).toMatch(/^\d{4}-\d{2}-01$/); // defaults to the first day of the current month
    expect(params.dueTo).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});

/** Mesma zona morta do #240: a coluna do badge parava a propagação sem pôr nada no lugar. */
describe('VendorHistory — célula de status', () => {
  const renderHistory = () => {
    renderWithProviders(
      <MemoryRouter>
        <VendorHistory vendor={vendor} />
      </MemoryRouter>,
    );
  };

  it('abre a solicitação quando o clique cai no badge de status', () => {
    renderHistory();

    fireEvent.click(screen.getByText('Aprovação'));

    expect(navigate).toHaveBeenCalledWith('/app/requisitions/r1');
  });

  it('abre o título quando o clique cai no badge de status da parcela', () => {
    renderHistory();

    fireEvent.click(screen.getByText('Agendada'));

    expect(navigate).toHaveBeenCalledWith('/app/payables/p1');
  });
});
