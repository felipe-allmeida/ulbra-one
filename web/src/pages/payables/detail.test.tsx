import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest';
import { act, cleanup, fireEvent, screen, within } from '@testing-library/react';
import { setViewportMatches } from '../../test/setup';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

const { mockUsePayable, mockUseAudit, mockMe, mockReject, mockReturn, mockCancel } = vi.hoisted(() => ({
  mockUsePayable: vi.fn(),
  mockUseAudit: vi.fn(),
  // Mutable identity; tests flip isAdmin/permissions to exercise the in-view gating.
  mockMe: { isAdmin: true, permissions: [] as string[] },
  mockReject: vi.fn(),
  mockReturn: vi.fn(),
  mockCancel: vi.fn(),
}));
// Os asides do papel trazem consultas próprias (fila, orçamento, conciliação) que não são o objeto
// deste teste — a tela de detalhe passou a montá-los ao adotar o AsideLayout.
vi.mock('./contabil-aside', () => ({ ContabilAside: () => <div data-testid='aside' /> }));
vi.mock('./tributacao-aside', () => ({ TributacaoAside: () => <div data-testid='aside' /> }));
vi.mock('../treasury/treasury-aside', () => ({ TreasuryAside: () => <div data-testid='aside' /> }));

vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    useParams: () => ({ id: 'payable_1' }),
    // BackLink/detail read location.state for origin-aware back; renderWithProviders has no Router.
    useLocation: () => ({ pathname: '/app/payables/payable_1', search: '', hash: '', state: null, key: 'default' }),
  };
});
vi.mock('../../queries', () => ({
  usePayable: mockUsePayable,
  usePayableAuditTrail: mockUseAudit,
  useApprovePayable: () => ({ mutate: vi.fn() }),
  useRejectPayable: () => ({ mutate: mockReject, isPending: false }),
  useReturnPayable: () => ({ mutate: mockReturn, isPending: false }),
  useCancelPayable: () => ({ mutate: mockCancel, isPending: false }),
  useDeletePayableAttachment: () => ({ mutate: vi.fn() }),
  useUploadPayableAttachment: () => ({ mutate: vi.fn(), isPending: false }),
  useCostCenters: () => ({ data: [] }),
  useExpenseCategories: () => ({ data: [] }),
  useLedgerAccounts: () => ({ data: [] }),
  // AEL-793: origin-requisition lookup (card only renders when the payable has an origin, which these
  // fixtures don't) — stub it so the module mock stays complete.
  useRequisition: () => ({ data: undefined }),
  useSetPayableWithholdings: () => ({ mutate: vi.fn(), isPending: false }),
  // BalanceForecastBanner (rendered on PendingApproval) uses this; loading short-circuits it harmlessly.
  useCashFlow: () => ({ isLoading: true }),
  // usePermissions() reads useMe(); default to Admin so the non-gating tests see every action.
  useMe: () => ({ data: mockMe }),
}));

import { PayableDetailPage } from './detail';

beforeEach(() => {
  // Este arquivo não usa clearAllMocks (as implementações são re-armadas aqui); as mutations são spies
  // compartilhados, então precisam ser zeradas à mão ou as chamadas vazam entre testes.
  mockReject.mockClear();
  mockReturn.mockClear();
  mockCancel.mockClear();
  mockMe.isAdmin = true;
  mockMe.permissions = [];
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
        {
          id: 'i1',
          sequence: 1,
          dueDate: '2026-06-30',
          amount: 1000,
          netAmount: 1000,
          status: 'Open',
          paidAmount: 0,
          payments: [],
        },
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

  it('shows the "Valor líquido" card and the per-installment net column once accounting completes', () => {
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
          {
            id: 'i1',
            sequence: 1,
            dueDate: '2026-06-30',
            amount: 1000,
            netAmount: 940,
            status: 'Open',
            paidAmount: 0,
            payments: [],
          },
        ],
        attachments: [],
        withholdings: [{ id: 'w1', taxType: 'Irrf', baseAmount: 1000, rate: null, amount: 60 }],
        totalWithheld: 60,
        netTotal: 940,
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
        id: 'payable_1',
        companyId: 'company_1',
        counterpartyName: 'ACME',
        description: 'NF 1',
        status: 'PendingApproval',
        totalAmount: 1000,
        costCenterId: 'cc_1',
        categoryId: null,
        accountingAccountId: null,
        documentNumber: 'NF-1',
        issueDate: '2026-06-01',
        installments: [
          {
            id: 'i1',
            sequence: 1,
            dueDate: '2026-06-30',
            amount: 1000,
            netAmount: 1000,
            status: 'Open',
            paidAmount: 0,
            payments: [],
          },
        ],
        attachments: [],
        withholdings: [],
        totalWithheld: 0,
        netTotal: 1000,
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.queryByText('Valor bruto')).toBeNull(); // installments keep the single amount column
  });

  it('keeps retentions read-only once scheduled (editor hidden — domain locks at Scheduled)', () => {
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
        installments: [],
        attachments: [],
        withholdings: [],
        totalWithheld: 0,
        netTotal: 1000,
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.queryByText('Adicionar retenção')).not.toBeInTheDocument();
  });

  it('enables the retentions editor before scheduling (PendingAccounting)', () => {
    mockUsePayable.mockReturnValue({
      data: {
        id: 'payable_1',
        companyId: 'company_1',
        counterpartyName: 'ACME',
        description: 'NF 1',
        status: 'PendingAccounting',
        totalAmount: 1000,
        costCenterId: 'cc_1',
        categoryId: null,
        accountingAccountId: null,
        documentNumber: 'NF-1',
        issueDate: '2026-06-01',
        installments: [],
        attachments: [],
        withholdings: [],
        totalWithheld: 0,
        netTotal: 1000,
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByText('Adicionar retenção')).toBeInTheDocument();
  });

  // ---- Gating in-view por cargo (fonte da verdade: RequirePermission no backend) ----

  const pendingApproval = {
    id: 'payable_1',
    companyId: 'company_1',
    counterpartyName: 'ACME',
    description: 'NF 1',
    status: 'PendingApproval',
    totalAmount: 1000,
    costCenterId: 'cc_1',
    categoryId: null,
    accountingAccountId: null,
    documentNumber: 'NF-1',
    issueDate: '2026-06-01',
    installments: [],
    attachments: [],
    withholdings: [],
    totalWithheld: 0,
    netTotal: 1000,
  };

  it('esconde Aprovar/Rejeitar sem payables:approve, mas mantém a leitura', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read'];
    mockUsePayable.mockReturnValue({ data: pendingApproval });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.queryByRole('button', { name: 'Aprovar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rejeitar' })).not.toBeInTheDocument();
    expect(screen.getByText(/ACME/)).toBeInTheDocument(); // detalhe (leitura) segue visível
  });

  it('mostra Aprovar/Rejeitar com payables:approve', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read', 'payables:approve'];
    mockUsePayable.mockReturnValue({ data: pendingApproval });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByRole('button', { name: 'Aprovar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Rejeitar' })).toBeInTheDocument();
  });

  it('esconde Editar/Cancelar sem payables:write (status editável)', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read', 'payables:approve'];
    mockUsePayable.mockReturnValue({ data: { ...pendingApproval, status: 'Received' } });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar' })).not.toBeInTheDocument();
  });

  // "Cancelar" virou "Rejeitar": para quem usa, os dois eram o mesmo desfecho terminal, e o botão
  // "Cancelar" ainda colidia com o "Cancelar" (= fechar) dos diálogos.
  it('mostra Editar/Rejeitar com payables:write', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read', 'payables:write'];
    mockUsePayable.mockReturnValue({ data: { ...pendingApproval, status: 'Received' } });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Rejeitar' })).toBeInTheDocument();
  });

  // Withholdings belong to Tax (payables:tax), not Accounting/Finance (payables:write) — AEL-948.
  it('esconde o editor de retenções sem payables:tax (payables:write puro)', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read', 'payables:write'];
    mockUsePayable.mockReturnValue({ data: { ...pendingApproval, status: 'PendingAccounting' } });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.queryByText('Adicionar retenção')).not.toBeInTheDocument();
  });

  it('mostra o editor de retenções com payables:tax (antes do agendamento)', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read', 'payables:tax'];
    mockUsePayable.mockReturnValue({ data: { ...pendingApproval, status: 'PendingAccounting' } });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByText('Adicionar retenção')).toBeInTheDocument();
  });

  it('esconde "Registrar pagamento" sem payables:pay (título agendado)', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read'];
    mockUsePayable.mockReturnValue({
      data: {
        ...pendingApproval,
        status: 'Scheduled',
        installments: [
          {
            id: 'i1',
            sequence: 1,
            dueDate: '2026-06-30',
            amount: 1000,
            netAmount: 1000,
            status: 'Open',
            paidAmount: 0,
            payments: [],
          },
        ],
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.queryByRole('button', { name: /Dar baixa/ })).not.toBeInTheDocument();
  });

  it('mostra "Registrar pagamento" com payables:pay', () => {
    mockMe.isAdmin = false;
    mockMe.permissions = ['payables:read', 'payables:pay'];
    mockUsePayable.mockReturnValue({
      data: {
        ...pendingApproval,
        status: 'Scheduled',
        installments: [
          {
            id: 'i1',
            sequence: 1,
            dueDate: '2026-06-30',
            amount: 1000,
            netAmount: 1000,
            status: 'Open',
            paidAmount: 0,
            payments: [],
          },
        ],
      },
    });
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByRole('button', { name: /Dar baixa/ })).toBeInTheDocument();
  });

  /**
   * O desfecho terminal é um botão só, mas dois endpoints: `cancel` exige payables:write e `reject`
   * exige payables:approve. Colapsar num só tiraria a saída de quem tem apenas uma das permissões.
   */
  describe('Rejeitar escolhe o endpoint pela permissão', () => {
    const openReject = () => {
      fireEvent.click(screen.getByRole('button', { name: 'Rejeitar' }));
      const dialog = screen.getByRole('alertdialog');
      fireEvent.change(within(dialog).getByPlaceholderText('Motivo da rejeição'), {
        target: { value: 'fora do orçamento' },
      });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Rejeitar título' }));
    };

    it('com payables:write usa cancel', () => {
      mockMe.isAdmin = false;
      mockMe.permissions = ['payables:read', 'payables:write'];
      mockUsePayable.mockReturnValue({ data: { ...pendingApproval, status: 'Received' } });
      renderWithProviders(<PayableDetailPage />);
      openReject();
      expect(mockCancel).toHaveBeenCalledWith({ id: 'payable_1', reason: 'fora do orçamento' }, expect.anything());
      expect(mockReject).not.toHaveBeenCalled();
    });

    it('só com payables:approve cai em reject', () => {
      mockMe.isAdmin = false;
      mockMe.permissions = ['payables:read', 'payables:approve'];
      mockUsePayable.mockReturnValue({ data: pendingApproval });
      renderWithProviders(<PayableDetailPage />);
      openReject();
      expect(mockReject).toHaveBeenCalledWith({ id: 'payable_1', reason: 'fora do orçamento' }, expect.anything());
      expect(mockCancel).not.toHaveBeenCalled();
    });

    it('sem motivo o confirmar fica travado', () => {
      mockMe.isAdmin = true;
      mockUsePayable.mockReturnValue({ data: { ...pendingApproval, status: 'Received' } });
      renderWithProviders(<PayableDetailPage />);
      fireEvent.click(screen.getByRole('button', { name: 'Rejeitar' }));
      const dialog = screen.getByRole('alertdialog');
      expect(within(dialog).getByRole('button', { name: 'Rejeitar título' })).toBeDisabled();
    });
  });

  // O painel só é sticky onde o aside é sticky; abaixo de 1200px ele desce para a coluna única.
  describe('colocação do painel', () => {
    afterEach(() => {
      cleanup();
      act(() => setViewportMatches(false));
    });

    it('em telas largas encabeça o aside', () => {
      act(() => setViewportMatches(true));
      mockUsePayable.mockReturnValue({ data: pendingApproval });
      const { container } = renderWithProviders(<PayableDetailPage />);
      expect(container.querySelector('.aside-layout-side .spanel-decision')).not.toBeNull();
      expect(container.querySelector('.aside-layout-main .spanel-decision')).toBeNull();
    });

    it('abaixo de 1200px vai para a coluna principal', () => {
      mockUsePayable.mockReturnValue({ data: pendingApproval });
      const { container } = renderWithProviders(<PayableDetailPage />);
      expect(container.querySelector('.aside-layout-main .spanel-decision')).not.toBeNull();
      expect(container.querySelector('.aside-layout-side .spanel-decision')).toBeNull();
    });
  });
});
