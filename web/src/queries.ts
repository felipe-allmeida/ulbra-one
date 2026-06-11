import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationOptions,
} from '@tanstack/react-query';
import {
  api,
  type BankAccount,
  type BankAccountInput,
  type BankAccountUpdateInput,
  type BankTransaction,
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
  type RegisterPaymentInput,
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
  expenseCategories: (includeInactive = false) => ['expenseCategories', includeInactive] as const,
  bankAccounts: (companyId: string, includeInactive = false) => ['bankAccounts', companyId, includeInactive] as const,
  statementImports: (companyId?: string, bankAccountId?: string) =>
    ['statementImports', companyId ?? '', bankAccountId ?? ''] as const,
  bankTransactions: (params: BankTransactionsParams) => ['bankTransactions', params] as const,
};

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
