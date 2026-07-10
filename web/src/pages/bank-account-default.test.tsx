/**
 * Tests for SP-3a frontend changes:
 * (a) BankAccountForm — "Conta padrão de pagamento" checkbox + method select
 *     - checkbox renders and is initially unchecked
 *     - when checked, method select appears
 *     - submitting with isDefault=true sends isDefaultForPayments: true + defaultPaymentMethod
 * (b) PaymentHistory — "Automático (sistema)" tag for system-actor payments
 *     - shows the tag when createdById === SYSTEM_ACTOR_ID
 *     - does NOT show the tag for a normal user payment
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n';

// ---- react-router-dom (BankAccountsPage doesn't need routing but imports pull it) ----
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn(), useParams: () => ({}) };
});

// ---- store ----
vi.mock('../store', async (orig) => {
  const actual = await orig<typeof import('../store')>();
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

// ---- queries ----
const { mockSaveBankAccount } = vi.hoisted(() => ({ mockSaveBankAccount: vi.fn() }));

vi.mock('../queries', async (orig) => {
  const actual = await orig<typeof import('../queries')>();
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
    useBankAccounts: () => ({ data: [], isLoading: false }),
    useSaveBankAccount: mockSaveBankAccount,
    useDeleteBankAccount: noopMut,
    useReversePayment: noopMut,
    usePayable: () => ({ data: undefined, isLoading: false }),
    useVendors: () => ({ data: { items: [] }, isLoading: false }),
    useCostCenters: () => ({ data: [] }),
    useExpenseCategories: () => ({ data: [] }),
    useLedgerAccounts: () => ({ data: [] }),
    useCreatePayable: noopMut,
    useUpdatePayable: noopMut,
    useUploadPayableAttachment: noopMut,
    useUploadPayableFiscalDocument: noopMut,
    useRequestFiscalUploadUrl: () => noopMut(),
    useParseFiscalDocument: () => noopMut(),
    uploadToPresignedUrl: vi.fn(),
    useApprovePayable: noopMut,
    useRejectPayable: noopMut,
    useCancelPayable: noopMut,
    useRegisterPayment: noopMut,
    useUploadPaymentProof: noopMut,
    useDeletePayableAttachment: noopMut,
    useClassificationSuggestion: () => ({ data: undefined }),
  };
});

import { BankAccountsPage } from './bank-accounts';
import { PaymentHistory } from './payables/payment-history';
import type { Payable, Installment, Payment } from '../api';
import { SYSTEM_ACTOR_ID } from '../lib';

// ============================================================
// (a) BankAccountForm — default payment toggle
// ============================================================

describe('BankAccountForm — default payment toggle', () => {
  let saveMutate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.clearAllMocks();
    saveMutate = vi.fn();
    mockSaveBankAccount.mockReturnValue({
      mutate: saveMutate,
      mutateAsync: vi.fn(),
      isPending: false,
      error: null,
      reset: vi.fn(),
      isSuccess: false,
      data: undefined,
    });
  });

  function openCreateDrawer() {
    renderWithProviders(<BankAccountsPage />);
    // Click "Nova conta bancária" button to open the drawer
    const newBtn = screen.getByRole('button', { name: /nova conta bancária/i });
    fireEvent.click(newBtn);
  }

  it('renders the isDefaultForPayments checkbox (unchecked by default)', async () => {
    openCreateDrawer();

    await waitFor(() => {
      expect(screen.getByText('Conta padrão de pagamento')).toBeInTheDocument();
    });

    const checkbox = screen.getByRole('checkbox');
    expect(checkbox).not.toBeChecked();
  });

  it('shows the payment method select when the checkbox is checked', async () => {
    openCreateDrawer();

    await waitFor(() => {
      expect(screen.getByRole('checkbox')).toBeInTheDocument();
    });

    const checkbox = screen.getByRole('checkbox');
    // Method select should not be visible yet
    expect(screen.queryByText('Forma de pagamento padrão')).toBeNull();

    fireEvent.click(checkbox);

    await waitFor(() => {
      expect(screen.getByText('Forma de pagamento padrão')).toBeInTheDocument();
    });
  });

  it('submitting with isDefault=true sends isDefaultForPayments:true + defaultPaymentMethod', async () => {
    openCreateDrawer();

    await waitFor(() => {
      expect(screen.getByRole('checkbox')).toBeInTheDocument();
    });

    // Fill required fields
    const aliasInput = screen.getByPlaceholderText('ex.: Conta principal');
    fireEvent.change(aliasInput, { target: { value: 'Conta Principal' } });

    const bankNameInput = screen.getByPlaceholderText('ex.: Banco do Brasil');
    fireEvent.change(bankNameInput, { target: { value: 'Bradesco' } });

    const bankCodeInput = screen.getByPlaceholderText('ex.: 001');
    fireEvent.change(bankCodeInput, { target: { value: '237' } });

    const agencyInput = screen.getByPlaceholderText('ex.: 1234');
    fireEvent.change(agencyInput, { target: { value: '1234' } });

    const accountInput = screen.getByPlaceholderText('ex.: 56789');
    fireEvent.change(accountInput, { target: { value: '56789' } });

    // Check the default checkbox — auto-sets defaultPaymentMethod to 'Pix' and reveals the method select
    const checkbox = screen.getByRole('checkbox');
    fireEvent.click(checkbox);

    await waitFor(() => {
      expect(screen.getByText('Forma de pagamento padrão')).toBeInTheDocument();
    });

    // Change the method to Ted to assert different from default Pix
    const methodSelects = screen.getAllByRole('combobox');
    const methodSelect = methodSelects.find((el) => {
      const opts = Array.from((el as HTMLSelectElement).options);
      return opts.some((o) => o.value === 'Pix');
    }) as HTMLSelectElement;
    fireEvent.change(methodSelect, { target: { value: 'Ted' } });

    // Form should be valid — submit it (button label is "Salvar")
    const saveBtn = screen.getByRole('button', { name: /^salvar$/i });
    fireEvent.click(saveBtn);

    await waitFor(() => {
      expect(saveMutate).toHaveBeenCalled();
    });

    const callArg = saveMutate.mock.calls[0][0] as {
      create: { isDefaultForPayments: boolean; defaultPaymentMethod: string };
    };
    expect(callArg.create.isDefaultForPayments).toBe(true);
    // defaultPaymentMethod is what the onValid handler sends (it derives it from form values)
    expect(callArg.create.defaultPaymentMethod).toBe('Ted');
  });
});

// ============================================================
// (b) PaymentHistory — auto-payment marker
// ============================================================

const basePayable: Payable = {
  id: 'payable_1',
  companyId: 'company_1',
  origin: 'Manual',
  vendorId: 'vendor_1',
  counterpartyName: 'Fornecedor Teste',
  documentNumber: null,
  issueDate: null,
  description: 'Energia elétrica',
  costCenterId: 'cc_1',
  categoryId: null,
  totalAmount: 500,
  status: 'Paid',
  rejectionReason: null,
  cancelReason: null,
  approvedBy: null,
  approvedByName: null,
  approvedAt: null,
  rejectedBy: null,
  rejectedByName: null,
  rejectedAt: null,
  accountedBy: null,
  accountedByName: null,
  accountedAt: null,
  accountingAccountId: null,
  isClassified: false,
  suggestedAccountingAccountId: null,
  classificationConfidence: null,
  classificationRationale: null,
  paymentBankAccountId: null,
  paymentMethod: 'Pix' as const, // non-null placeholder; value unused by test
  type: 'Service',
  createdAt: '2025-06-01T00:00:00Z',
  installments: [],
  attachments: [],
  withholdings: [],
  totalWithheld: 0,
  netTotal: 500,
};

function makePayment(overrides: Partial<Payment> = {}): Payment {
  return {
    id: 'pay_1',
    installmentId: 'inst_1',
    paymentDate: '2025-06-15',
    amount: 500,
    bankAccountId: 'ba_1',
    method: 'Pix',
    interest: 0,
    fine: 0,
    discount: 0,
    notes: null,
    isReversed: false,
    reversalReason: null,
    hasProof: false,
    createdAt: '2025-06-15T10:00:00Z',
    createdById: 'user-normal-id',
    ...overrides,
  };
}

function makeInstallment(payments: Payment[]): Installment {
  return {
    id: 'inst_1',
    sequence: 1,
    dueDate: '2025-06-15',
    amount: 500,
    paidAmount: 500,
    status: 'Paid',
    payments,
    netAmount: 500,
    scheduledPaymentDate: '2025-06-15',
    isHeld: false,
    holdReason: null,
    rail: 'Manual',
    manualReason: null,
    remittanceId: null,
    remittanceSequence: null,
  };
}

describe('PaymentHistory — auto-payment marker', () => {
  it('shows "Automático (sistema)" tag when createdById === SYSTEM_ACTOR_ID', () => {
    const autoPayment = makePayment({ createdById: SYSTEM_ACTOR_ID });
    const installment = makeInstallment([autoPayment]);

    renderWithProviders(<PaymentHistory payable={basePayable} installment={installment} />);

    expect(screen.getByText('Automático (sistema)')).toBeInTheDocument();
  });

  it('does NOT show the auto-pay tag for a normal user payment', () => {
    const normalPayment = makePayment({ createdById: 'user-abc-normal-id' });
    const installment = makeInstallment([normalPayment]);

    renderWithProviders(<PaymentHistory payable={basePayable} installment={installment} />);

    expect(screen.queryByText('Automático (sistema)')).toBeNull();
  });
});
