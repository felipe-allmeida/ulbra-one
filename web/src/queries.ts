import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationOptions,
} from '@tanstack/react-query';
import {
  api,
  type AccountGroup,
  type AccountKind,
  type AgingBucket,
  type JournalEntry,
  type JournalEntryInput,
  type JournalStatus,
  type LedgerAccount,
  type LedgerAccountInput,
  type LedgerAccountKind,
  type LedgerAccountUpdateInput,
  type BankAccount,
  type BankAccountInput,
  type BankAccountUpdateInput,
  type BankTransaction,
  type ChartAccount,
  type ChartType,
  type ClassificationPolicy,
  type Company,
  type CostCenter,
  type Customer,
  type CustomerInput,
  type Employee,
  type EmployeeInput,
  type ExpenseCategory,
  type MatchStatus,
  type Payable,
  type PayableAttachment,
  type PayableInput,
  type PayableStatus,
  type Payment,
  type Receipt,
  type Receivable,
  type ReceivableInput,
  type ReceivableStatus,
  type RegisterPaymentInput,
  type RegisterReceiptInput,
  type StatementImport,
  type Transaction,
  type TransactionStatus,
  type TransactionType,
  type Unit,
  type Vendor,
  type VendorInput,
} from './api';

/** Stable query-key factory — single source of truth for cache keys + invalidation. */
export const queryKeys = {
  companies: ['companies'] as const,
  me: ['me'] as const,
  units: (companyId: string) => ['units', companyId] as const,
  costCenters: (companyId: string) => ['costCenters', companyId] as const,
  costCenter: (id: string) => ['costCenter', id] as const,
  transactions: (params: TransactionsParams) => ['transactions', params] as const,
  transaction: (id: string) => ['transaction', id] as const,
  vendors: (companyId: string, search?: string, page = 1) => ['vendors', companyId, search ?? '', page] as const,
  customers: (companyId: string, search?: string, page = 1) => ['customers', companyId, search ?? '', page] as const,
  employees: (companyId: string, search?: string, page = 1) => ['employees', companyId, search ?? '', page] as const,
  payables: (params: PayablesParams) => ['payables', params] as const,
  payable: (id: string) => ['payable', id] as const,
  receivables: (params: ReceivablesParams) => ['receivables', params] as const,
  receivable: (id: string) => ['receivable', id] as const,
  expenseCategories: (includeInactive = false) => ['expenseCategories', includeInactive] as const,
  bankAccounts: (companyId: string, includeInactive = false) => ['bankAccounts', companyId, includeInactive] as const,
  statementImports: (companyId?: string, bankAccountId?: string) =>
    ['statementImports', companyId ?? '', bankAccountId ?? ''] as const,
  bankTransactions: (params: BankTransactionsParams) => ['bankTransactions', params] as const,
  chartAccounts: (params: ChartAccountsParams) => ['chartAccounts', params] as const,
  classificationPolicy: (companyId: string) => ['classificationPolicy', companyId] as const,
  aging: (companyId: string, asOf?: string) => ['aging', companyId, asOf ?? ''] as const,
  agingItems: (params: AgingItemsParams) => ['agingItems', params] as const,
  ledgerAccounts: (params: LedgerAccountsParams) => ['ledgerAccounts', params] as const,
  journalEntries: (params: JournalEntriesParams) => ['journalEntries', params] as const,
  journalEntry: (id: string) => ['journalEntry', id] as const,
  trialBalance: (companyId?: string) => ['trialBalance', companyId ?? ''] as const,
  incomeStatement: (params: IncomeStatementParams) => ['incomeStatement', params] as const,
  balanceSheet: (params: BalanceSheetParams) => ['balanceSheet', params] as const,
};

export interface LedgerAccountsParams {
  companyId?: string;
  accountGroup?: AccountGroup;
  kind?: LedgerAccountKind;
  activeOnly?: boolean;
}

export interface JournalEntriesParams {
  companyId?: string;
  status?: JournalStatus;
  from?: string;
  to?: string;
}

export interface IncomeStatementParams {
  companyId?: string;
  from?: string;
  to?: string;
}

export interface BalanceSheetParams {
  companyId?: string;
  asOf?: string;
}

export interface BankTransactionsParams {
  companyId?: string;
  bankAccountId?: string;
  importId?: string;
  matchStatus?: MatchStatus;
  dueFrom?: string;
  dueTo?: string;
  page?: number;
  pageSize?: number;
}

export interface PayablesParams {
  companyId?: string;
  status?: PayableStatus;
  vendorId?: string;
  costCenterId?: string;
  categoryId?: string;
  dueFrom?: string;
  dueTo?: string;
  paid?: boolean;
  classified?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ReceivablesParams {
  companyId?: string;
  status?: ReceivableStatus;
  payerId?: string;
  costCenterId?: string;
  dueFrom?: string;
  dueTo?: string;
  received?: boolean;
  page?: number;
  pageSize?: number;
}

export interface TransactionsParams {
  companyId?: string;
  type?: TransactionType;
  status?: TransactionStatus;
  costCenterId?: string;
  page?: number;
  pageSize?: number;
}

export interface ChartAccountsParams {
  chartType?: ChartType;
  kind?: AccountKind;
  activeOnly?: boolean;
}

export interface AgingItemsParams {
  companyId: string;
  bucket?: AgingBucket;
  vendorId?: string;
  costCenterId?: string;
  asOf?: string;
}

/* ===================== Queries ===================== */

export function useCompanies() {
  return useQuery({ queryKey: queryKeys.companies, queryFn: () => api.listCompanies() });
}

export function useMe() {
  return useQuery({ queryKey: queryKeys.me, queryFn: () => api.me() });
}

export function useUnits(companyId: string) {
  return useQuery({
    queryKey: queryKeys.units(companyId),
    queryFn: () => api.listUnits(companyId),
    enabled: !!companyId,
  });
}

export function useCostCenters(companyId: string) {
  return useQuery({
    queryKey: queryKeys.costCenters(companyId),
    queryFn: () => api.listCostCenters(companyId),
    enabled: !!companyId,
  });
}

export function useCostCenter(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.costCenter(id ?? ''),
    queryFn: () => api.getCostCenter(id!),
    enabled: !!id,
  });
}

export function useTransactions(params: TransactionsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.transactions(params),
    queryFn: () => api.listTransactions(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useTransaction(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.transaction(id ?? ''),
    queryFn: () => api.getTransaction(id!),
    enabled: !!id,
  });
}

export function useVendors(companyId: string, search?: string, page = 1) {
  return useQuery({
    queryKey: queryKeys.vendors(companyId, search, page),
    queryFn: () => api.listVendors(companyId, search, page),
    enabled: !!companyId,
    placeholderData: keepPreviousData,
  });
}

export function useCustomers(companyId: string, search?: string, page = 1) {
  return useQuery({
    queryKey: queryKeys.customers(companyId, search, page),
    queryFn: () => api.listCustomers(companyId, search, page),
    enabled: !!companyId,
    placeholderData: keepPreviousData,
  });
}

export function useEmployees(companyId: string, search?: string, page = 1) {
  return useQuery({
    queryKey: queryKeys.employees(companyId, search, page),
    queryFn: () => api.listEmployees(companyId, search, page),
    enabled: !!companyId,
    placeholderData: keepPreviousData,
  });
}

export function usePayables(params: PayablesParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.payables(params),
    queryFn: () => api.listPayables(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function usePayable(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.payable(id ?? ''),
    queryFn: () => api.getPayable(id!),
    enabled: !!id,
  });
}

export function useReceivables(params: ReceivablesParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.receivables(params),
    queryFn: () => api.listReceivables(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useReceivable(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.receivable(id ?? ''),
    queryFn: () => api.getReceivable(id!),
    enabled: !!id,
  });
}

export function useExpenseCategories(includeInactive = false) {
  return useQuery({
    queryKey: queryKeys.expenseCategories(includeInactive),
    queryFn: () => api.listExpenseCategories(includeInactive),
  });
}

export function useBankAccounts(companyId: string, includeInactive = false) {
  return useQuery({
    queryKey: queryKeys.bankAccounts(companyId, includeInactive),
    queryFn: () => api.listBankAccounts(companyId, includeInactive),
    enabled: !!companyId,
  });
}

export function useStatementImports(companyId?: string, bankAccountId?: string) {
  return useQuery({
    queryKey: queryKeys.statementImports(companyId, bankAccountId),
    queryFn: () => api.listStatementImports(companyId, bankAccountId),
  });
}

export function useBankTransactions(params: BankTransactionsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.bankTransactions(params),
    queryFn: () => api.listBankTransactions(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

/* ===================== Mutations ===================== */

/** Thin wrapper so call sites can pass an `onSuccess` while we own cache invalidation. */
function useInvalidatingMutation<TData, TVars>(
  mutationFn: (vars: TVars) => Promise<TData>,
  invalidate: (qc: ReturnType<typeof useQueryClient>, data: TData, vars: TVars) => void,
  options?: UseMutationOptions<TData, unknown, TVars>,
) {
  const qc = useQueryClient();
  return useMutation<TData, unknown, TVars>({
    mutationFn,
    ...options,
    onSuccess: (data, vars, ...rest) => {
      invalidate(qc, data, vars);
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Companies ---------- */
export function useCreateCompany(options?: UseMutationOptions<Company, unknown, { legalName: string; cnpj: string }>) {
  return useInvalidatingMutation(
    ({ legalName, cnpj }) => api.createCompany(legalName, cnpj),
    (qc) => void qc.invalidateQueries({ queryKey: queryKeys.companies }),
    options,
  );
}

export function useUpdateCompany(
  options?: UseMutationOptions<Company, unknown, { id: string; legalName: string; cnpj: string }>,
) {
  return useInvalidatingMutation(
    ({ id, legalName, cnpj }) => api.updateCompany(id, legalName, cnpj),
    (qc) => void qc.invalidateQueries({ queryKey: queryKeys.companies }),
    options,
  );
}

/* ---------- Units ---------- */
export function useCreateUnit(
  options?: UseMutationOptions<Unit, unknown, { companyId: string; name: string; acronym: string }>,
) {
  return useInvalidatingMutation(
    ({ companyId, name, acronym }) => api.createUnit(companyId, name, acronym),
    (qc, data) => void qc.invalidateQueries({ queryKey: queryKeys.units(data.companyId) }),
    options,
  );
}

export function useUpdateUnit(
  options?: UseMutationOptions<Unit, unknown, { id: string; name: string; acronym: string }>,
) {
  return useInvalidatingMutation(
    ({ id, name, acronym }) => api.updateUnit(id, name, acronym),
    (qc, data) => void qc.invalidateQueries({ queryKey: queryKeys.units(data.companyId) }),
    options,
  );
}

/* ---------- Cost centers ---------- */
export interface CostCenterVars {
  id?: string;
  companyId: string;
  code: string;
  name: string;
  annualBudget: number;
  autoApprovalLimit: number;
  unitIds: string[];
}

export function useSaveCostCenter(options?: UseMutationOptions<CostCenter, unknown, CostCenterVars>) {
  return useInvalidatingMutation(
    (v) =>
      v.id
        ? api.updateCostCenter(v.id, v.code, v.name, v.annualBudget, v.autoApprovalLimit, v.unitIds)
        : api.createCostCenter(v.companyId, v.code, v.name, v.annualBudget, v.autoApprovalLimit, v.unitIds),
    (qc, data) => {
      void qc.invalidateQueries({ queryKey: queryKeys.costCenters(data.companyId) });
      void qc.invalidateQueries({ queryKey: queryKeys.costCenter(data.id) });
    },
    options,
  );
}

/* ---------- Transactions ---------- */
export function useCreateTransaction(
  options?: UseMutationOptions<
    Transaction,
    unknown,
    {
      companyId: string;
      unitId: string;
      costCenterId: string;
      type: TransactionType;
      description: string;
      vendorId?: string | null;
      customerId?: string | null;
      amount: number;
      dueDate: string;
    }
  >,
) {
  return useInvalidatingMutation(
    (body) => api.createTransaction(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['transactions'] }),
    options,
  );
}

/** Invalidates list + single caches after a transaction state change. */
function invalidateTransaction(qc: ReturnType<typeof useQueryClient>, data: Transaction) {
  void qc.invalidateQueries({ queryKey: ['transactions'] });
  void qc.invalidateQueries({ queryKey: queryKeys.transaction(data.id) });
}

export function useApproveTransaction(options?: UseMutationOptions<Transaction, unknown, { id: string }>) {
  return useInvalidatingMutation(({ id }) => api.approveTransaction(id), invalidateTransaction, options);
}

export function useRejectTransaction(
  options?: UseMutationOptions<Transaction, unknown, { id: string; reason: string }>,
) {
  return useInvalidatingMutation(({ id, reason }) => api.rejectTransaction(id, reason), invalidateTransaction, options);
}

export function usePayTransaction(options?: UseMutationOptions<Transaction, unknown, { id: string }>) {
  return useInvalidatingMutation(({ id }) => api.payTransaction(id), invalidateTransaction, options);
}

/* ---------- Vendors ---------- */
export function useSaveVendor(options?: UseMutationOptions<Vendor, unknown, { id?: string; body: VendorInput }>) {
  return useInvalidatingMutation(
    ({ id, body }) => (id ? api.updateVendor(id, body) : api.createVendor(body)),
    (qc, data) => void qc.invalidateQueries({ queryKey: ['vendors', data.companyId] }),
    options,
  );
}

export function useVendorAction(
  options?: UseMutationOptions<unknown, unknown, { id: string; action: 'activate' | 'deactivate' | 'delete' }>,
) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'activate' | 'deactivate' | 'delete' }) =>
      action === 'delete'
        ? api.removeVendor(id)
        : action === 'activate'
          ? api.activateVendor(id)
          : api.deactivateVendor(id),
    ...options,
    onSuccess: (data, vars, ...rest) => {
      void qc.invalidateQueries({ queryKey: ['vendors'] });
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Customers ---------- */
export function useSaveCustomer(options?: UseMutationOptions<Customer, unknown, { id?: string; body: CustomerInput }>) {
  return useInvalidatingMutation(
    ({ id, body }) => (id ? api.updateCustomer(id, body) : api.createCustomer(body)),
    (qc, data) => void qc.invalidateQueries({ queryKey: ['customers', data.companyId] }),
    options,
  );
}

export function useCustomerAction(
  options?: UseMutationOptions<unknown, unknown, { id: string; action: 'activate' | 'deactivate' | 'delete' }>,
) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'activate' | 'deactivate' | 'delete' }) =>
      action === 'delete'
        ? api.removeCustomer(id)
        : action === 'activate'
          ? api.activateCustomer(id)
          : api.deactivateCustomer(id),
    ...options,
    onSuccess: (data, vars, ...rest) => {
      void qc.invalidateQueries({ queryKey: ['customers'] });
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Employees ---------- */
export function useSaveEmployee(options?: UseMutationOptions<Employee, unknown, { id?: string; body: EmployeeInput }>) {
  return useInvalidatingMutation(
    ({ id, body }) => (id ? api.updateEmployee(id, body) : api.createEmployee(body)),
    (qc, data) => void qc.invalidateQueries({ queryKey: ['employees', data.companyId] }),
    options,
  );
}

export function useEmployeeAction(
  options?: UseMutationOptions<unknown, unknown, { id: string; action: 'activate' | 'deactivate' | 'delete' }>,
) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'activate' | 'deactivate' | 'delete' }) =>
      action === 'delete'
        ? api.removeEmployee(id)
        : action === 'activate'
          ? api.activateEmployee(id)
          : api.deactivateEmployee(id),
    ...options,
    onSuccess: (data, vars, ...rest) => {
      void qc.invalidateQueries({ queryKey: ['employees'] });
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Payables ---------- */
/** Invalidates the payables lists + the single payable cache after a state change. */
function invalidatePayable(qc: ReturnType<typeof useQueryClient>, data: Payable) {
  void qc.invalidateQueries({ queryKey: ['payables'] });
  void qc.invalidateQueries({ queryKey: queryKeys.payable(data.id) });
}

export function useCreatePayable(options?: UseMutationOptions<Payable, unknown, PayableInput>) {
  return useInvalidatingMutation(
    (body) => api.createPayable(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['payables'] }),
    options,
  );
}

export function useUpdatePayable(
  options?: UseMutationOptions<Payable, unknown, { id: string; body: Omit<PayableInput, 'origin'> }>,
) {
  return useInvalidatingMutation(({ id, body }) => api.updatePayable(id, body), invalidatePayable, options);
}

export function useApprovePayable(options?: UseMutationOptions<Payable, unknown, { id: string }>) {
  return useInvalidatingMutation(({ id }) => api.approvePayable(id), invalidatePayable, options);
}

export function useRejectPayable(options?: UseMutationOptions<Payable, unknown, { id: string; reason: string }>) {
  return useInvalidatingMutation(({ id, reason }) => api.rejectPayable(id, reason), invalidatePayable, options);
}

export function useCancelPayable(options?: UseMutationOptions<Payable, unknown, { id: string; reason: string }>) {
  return useInvalidatingMutation(({ id, reason }) => api.cancelPayable(id, reason), invalidatePayable, options);
}

export function useUploadPayableAttachment(
  options?: UseMutationOptions<PayableAttachment, unknown, { id: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ id, file }) => api.uploadPayableAttachment(id, file),
    (qc, _data, vars) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
    },
    options,
  );
}

export function useDeletePayableAttachment(
  options?: UseMutationOptions<void, unknown, { id: string; attachmentId: string }>,
) {
  return useInvalidatingMutation(
    ({ id, attachmentId }) => api.deletePayableAttachment(id, attachmentId),
    (qc, _data, vars) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
    },
    options,
  );
}

/* ---------- Expense categories ---------- */
export function useSaveExpenseCategory(
  options?: UseMutationOptions<ExpenseCategory, unknown, { id?: string; name: string; active?: boolean }>,
) {
  return useInvalidatingMutation(
    ({ id, name, active }) =>
      id ? api.updateExpenseCategory(id, name, active ?? true) : api.createExpenseCategory(name),
    (qc) => void qc.invalidateQueries({ queryKey: ['expenseCategories'] }),
    options,
  );
}

export function useDeleteExpenseCategory(options?: UseMutationOptions<void, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.deleteExpenseCategory(id),
    (qc) => void qc.invalidateQueries({ queryKey: ['expenseCategories'] }),
    options,
  );
}

/* ---------- Bank accounts ---------- */
export function useSaveBankAccount(
  options?: UseMutationOptions<
    BankAccount,
    unknown,
    { id?: string; create?: BankAccountInput; update?: BankAccountUpdateInput }
  >,
) {
  return useInvalidatingMutation(
    ({ id, create, update }) => (id ? api.updateBankAccount(id, update!) : api.createBankAccount(create!)),
    (qc, data) => void qc.invalidateQueries({ queryKey: queryKeys.bankAccounts(data.companyId) }),
    options,
  );
}

export function useDeleteBankAccount(options?: UseMutationOptions<void, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.deleteBankAccount(id),
    (qc) => void qc.invalidateQueries({ queryKey: ['bankAccounts'] }),
    options,
  );
}

/* ---------- Payments (baixa) ---------- */
/** Invalidates the payables lists + the single payable cache after a payment change. */
function invalidatePayablePayment(qc: ReturnType<typeof useQueryClient>, payableId: string) {
  void qc.invalidateQueries({ queryKey: ['payables'] });
  void qc.invalidateQueries({ queryKey: queryKeys.payable(payableId) });
}

export function useRegisterPayment(
  options?: UseMutationOptions<
    Payable,
    unknown,
    { payableId: string; installmentId: string; body: RegisterPaymentInput }
  >,
) {
  return useInvalidatingMutation(
    ({ payableId, installmentId, body }) => api.registerPayment(payableId, installmentId, body),
    (qc, _data, vars) => invalidatePayablePayment(qc, vars.payableId),
    options,
  );
}

export function useReversePayment(
  options?: UseMutationOptions<Payable, unknown, { payableId: string; paymentId: string; reason: string }>,
) {
  return useInvalidatingMutation(
    ({ payableId, paymentId, reason }) => api.reversePayment(payableId, paymentId, reason),
    (qc, _data, vars) => invalidatePayablePayment(qc, vars.payableId),
    options,
  );
}

export function useUploadPaymentProof(
  options?: UseMutationOptions<Payment, unknown, { payableId: string; paymentId: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ payableId, paymentId, file }) => api.uploadPaymentProof(payableId, paymentId, file),
    (qc, _data, vars) => invalidatePayablePayment(qc, vars.payableId),
    options,
  );
}

/* ---------- Receivables (Contas a Receber) ---------- */
/** Invalidates the receivables lists + the single receivable cache after a state change. */
function invalidateReceivable(qc: ReturnType<typeof useQueryClient>, data: Receivable) {
  void qc.invalidateQueries({ queryKey: ['receivables'] });
  void qc.invalidateQueries({ queryKey: queryKeys.receivable(data.id) });
}

export function useCreateReceivable(options?: UseMutationOptions<Receivable, unknown, ReceivableInput>) {
  return useInvalidatingMutation(
    (body) => api.createReceivable(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['receivables'] }),
    options,
  );
}

/* ---------- Conciliação OFX (statement imports + bank transactions) ---------- */
/** Invalidates the bank-transaction lists + the statement-import lists after a match/baixa change.
 * Also invalidates payables, since confirming/matching a transaction registers a baixa. */
function invalidateConciliation(qc: ReturnType<typeof useQueryClient>) {
  void qc.invalidateQueries({ queryKey: ['bankTransactions'] });
  void qc.invalidateQueries({ queryKey: ['statementImports'] });
  void qc.invalidateQueries({ queryKey: ['payables'] });
}

export function useImportStatement(
  options?: UseMutationOptions<StatementImport, unknown, { bankAccountId: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ bankAccountId, file }) => api.importStatement(bankAccountId, file),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['bankTransactions'] });
      void qc.invalidateQueries({ queryKey: ['statementImports'] });
    },
    options,
  );
}

export function useUpdateReceivable(
  options?: UseMutationOptions<Receivable, unknown, { id: string; body: Omit<ReceivableInput, 'origin'> }>,
) {
  return useInvalidatingMutation(({ id, body }) => api.updateReceivable(id, body), invalidateReceivable, options);
}

export function useCancelReceivable(options?: UseMutationOptions<Receivable, unknown, { id: string; reason: string }>) {
  return useInvalidatingMutation(({ id, reason }) => api.cancelReceivable(id, reason), invalidateReceivable, options);
}

/* ---------- Receipts (baixa de entrada) ---------- */
/** Invalidates the receivables lists + the single receivable cache after a receipt change. */
function invalidateReceivableReceipt(qc: ReturnType<typeof useQueryClient>, receivableId: string) {
  void qc.invalidateQueries({ queryKey: ['receivables'] });
  void qc.invalidateQueries({ queryKey: queryKeys.receivable(receivableId) });
}

export function useRegisterReceipt(
  options?: UseMutationOptions<
    Receivable,
    unknown,
    { receivableId: string; installmentId: string; body: RegisterReceiptInput }
  >,
) {
  return useInvalidatingMutation(
    ({ receivableId, installmentId, body }) => api.registerReceipt(receivableId, installmentId, body),
    (qc, _data, vars) => invalidateReceivableReceipt(qc, vars.receivableId),
    options,
  );
}

export function useConfirmMatch(options?: UseMutationOptions<BankTransaction, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.confirmMatch(id),
    (qc) => invalidateConciliation(qc),
    options,
  );
}

export function useMatchTransaction(
  options?: UseMutationOptions<BankTransaction, unknown, { id: string; installmentId: string }>,
) {
  return useInvalidatingMutation(
    ({ id, installmentId }) => api.matchTransaction(id, installmentId),
    (qc) => invalidateConciliation(qc),
    options,
  );
}

export function useIgnoreTransaction(options?: UseMutationOptions<BankTransaction, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.ignoreTransaction(id),
    (qc) => invalidateConciliation(qc),
    options,
  );
}

export function useUnmatchTransaction(options?: UseMutationOptions<BankTransaction, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.unmatchTransaction(id),
    (qc) => invalidateConciliation(qc),
    options,
  );
}

/* ---------- Chart of Accounts (Plano de Contas) ---------- */
export function useChartAccounts(params: ChartAccountsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.chartAccounts(params),
    queryFn: () => api.listChartAccounts(params),
    enabled: options?.enabled ?? true,
  });
}

export function useAging(companyId: string | undefined, asOf?: string) {
  return useQuery({
    queryKey: queryKeys.aging(companyId ?? '', asOf),
    queryFn: () => api.aging(companyId!, asOf),
    enabled: !!companyId,
  });
}

export function useAgingItems(params: AgingItemsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.agingItems(params),
    queryFn: () => api.agingItems(params),
    enabled: options?.enabled ?? !!params.companyId,
  });
}

export function useCreateChartAccount(
  options?: UseMutationOptions<ChartAccount, unknown, Parameters<typeof api.createChartAccount>[0]>,
) {
  return useInvalidatingMutation(
    (body) => api.createChartAccount(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['chartAccounts'] }),
    options,
  );
}

export function useUpdateChartAccount(
  options?: UseMutationOptions<
    ChartAccount,
    unknown,
    { id: string; body: Parameters<typeof api.updateChartAccount>[1] }
  >,
) {
  return useInvalidatingMutation(
    ({ id, body }) => api.updateChartAccount(id, body),
    (qc) => void qc.invalidateQueries({ queryKey: ['chartAccounts'] }),
    options,
  );
}

export function useReverseReceipt(
  options?: UseMutationOptions<Receivable, unknown, { receivableId: string; receiptId: string; reason: string }>,
) {
  return useInvalidatingMutation(
    ({ receivableId, receiptId, reason }) => api.reverseReceipt(receivableId, receiptId, reason),
    (qc, _data, vars) => invalidateReceivableReceipt(qc, vars.receivableId),
    options,
  );
}

export function useDeleteChartAccount(options?: UseMutationOptions<void, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.deleteChartAccount(id),
    (qc) => void qc.invalidateQueries({ queryKey: ['chartAccounts'] }),
    options,
  );
}

export function useUploadReceiptProof(
  options?: UseMutationOptions<Receipt, unknown, { receivableId: string; receiptId: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ receivableId, receiptId, file }) => api.uploadReceiptProof(receivableId, receiptId, file),
    (qc, _data, vars) => invalidateReceivableReceipt(qc, vars.receivableId),
    options,
  );
}

/* ---------- Classification ---------- */
export function useClassifyPayable(
  options?: UseMutationOptions<
    Payable,
    unknown,
    { id: string; body: { accountingAccountId?: string | null; financialAccountId?: string | null } }
  >,
) {
  return useInvalidatingMutation(
    ({ id, body }) => api.classifyPayable(id, body),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['chartAccounts'] });
    },
    options,
  );
}

export function useClassifyBatch(
  options?: UseMutationOptions<{ classifiedCount: number }, unknown, Parameters<typeof api.classifyBatch>[0]>,
) {
  return useInvalidatingMutation(
    (body) => api.classifyBatch(body),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['chartAccounts'] });
    },
    options,
  );
}

export function useClassificationSuggestion(payableId: string | undefined, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ['classificationSuggestion', payableId ?? ''],
    queryFn: () => api.classificationSuggestion(payableId!),
    enabled: (options?.enabled ?? true) && !!payableId,
  });
}

/* ---------- Classification Policies ---------- */
export function useClassificationPolicy(companyId: string) {
  return useQuery({
    queryKey: queryKeys.classificationPolicy(companyId),
    queryFn: () => api.getClassificationPolicy(companyId),
    enabled: !!companyId,
  });
}

export function useUpdateClassificationPolicy(
  options?: UseMutationOptions<
    ClassificationPolicy,
    unknown,
    { companyId: string; body: { requireToApprove: boolean; requireToPay: boolean } }
  >,
) {
  return useInvalidatingMutation(
    ({ companyId, body }) => api.updateClassificationPolicy(companyId, body),
    (qc, data) => void qc.invalidateQueries({ queryKey: queryKeys.classificationPolicy(data.companyId) }),
    options,
  );
}

/* ---------- Accounting: Ledger accounts (plano de contas contábil) ---------- */
export function useLedgerAccounts(params: LedgerAccountsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.ledgerAccounts(params),
    queryFn: () => api.listLedgerAccounts(params),
    enabled: options?.enabled ?? true,
  });
}

export function useCreateLedgerAccount(options?: UseMutationOptions<LedgerAccount, unknown, LedgerAccountInput>) {
  return useInvalidatingMutation(
    (body) => api.createLedgerAccount(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['ledgerAccounts'] }),
    options,
  );
}

export function useUpdateLedgerAccount(
  options?: UseMutationOptions<LedgerAccount, unknown, { id: string; body: LedgerAccountUpdateInput }>,
) {
  return useInvalidatingMutation(
    ({ id, body }) => api.updateLedgerAccount(id, body),
    (qc) => void qc.invalidateQueries({ queryKey: ['ledgerAccounts'] }),
    options,
  );
}

export function useSeedLedgerAccounts(options?: UseMutationOptions<LedgerAccount[], unknown, { companyId: string }>) {
  return useInvalidatingMutation(
    ({ companyId }) => api.seedLedgerAccounts(companyId),
    (qc) => void qc.invalidateQueries({ queryKey: ['ledgerAccounts'] }),
    options,
  );
}

/* ---------- Accounting: Journal entries (lançamentos) ---------- */
export function useJournalEntries(params: JournalEntriesParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.journalEntries(params),
    queryFn: () => api.listJournalEntries(params),
    enabled: options?.enabled ?? true,
  });
}

export function useJournalEntry(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.journalEntry(id ?? ''),
    queryFn: () => api.getJournalEntry(id!),
    enabled: !!id,
  });
}

/** Invalidates the journal lists + single cache + every accounting report after a change. */
function invalidateJournal(qc: ReturnType<typeof useQueryClient>, entryId?: string) {
  void qc.invalidateQueries({ queryKey: ['journalEntries'] });
  if (entryId) void qc.invalidateQueries({ queryKey: queryKeys.journalEntry(entryId) });
  void qc.invalidateQueries({ queryKey: ['trialBalance'] });
  void qc.invalidateQueries({ queryKey: ['incomeStatement'] });
  void qc.invalidateQueries({ queryKey: ['balanceSheet'] });
}

export function useCreateJournalEntry(options?: UseMutationOptions<JournalEntry, unknown, JournalEntryInput>) {
  return useInvalidatingMutation(
    (body) => api.createJournalEntry(body),
    (qc, data) => invalidateJournal(qc, data.id),
    options,
  );
}

export function useUpdateJournalEntry(
  options?: UseMutationOptions<JournalEntry, unknown, { id: string; body: Omit<JournalEntryInput, 'companyId'> }>,
) {
  return useInvalidatingMutation(
    ({ id, body }) => api.updateJournalEntry(id, body),
    (qc, data) => invalidateJournal(qc, data.id),
    options,
  );
}

export function usePostJournalEntry(options?: UseMutationOptions<JournalEntry, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.postJournalEntry(id),
    (qc, data) => invalidateJournal(qc, data.id),
    options,
  );
}

export function useReverseJournalEntry(
  options?: UseMutationOptions<JournalEntry, unknown, { id: string; reason: string; competenceDate?: string | null }>,
) {
  return useInvalidatingMutation(
    ({ id, reason, competenceDate }) => api.reverseJournalEntry(id, reason, competenceDate),
    (qc, _data, vars) => invalidateJournal(qc, vars.id),
    options,
  );
}

/* ---------- Accounting: Reports ---------- */
export function useTrialBalance(companyId?: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.trialBalance(companyId),
    queryFn: () => api.trialBalance(companyId),
    enabled: options?.enabled ?? true,
  });
}

export function useIncomeStatement(params: IncomeStatementParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.incomeStatement(params),
    queryFn: () => api.incomeStatement(params),
    enabled: options?.enabled ?? true,
  });
}

export function useBalanceSheet(params: BalanceSheetParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.balanceSheet(params),
    queryFn: () => api.balanceSheet(params),
    enabled: options?.enabled ?? true,
  });
}
