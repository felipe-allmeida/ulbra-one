import { apiErrorFromProblem } from './apiError';
import { getApiBaseUrl } from './config/env';
import type { components } from './api/schema';

/** All generated OpenAPI component schemas. Source of truth for wire types. */
type Schemas = components['schemas'];

const BASE_URL = getApiBaseUrl();

const TOKEN_KEY = 'ulbra-one.token';

export const auth = {
  get token() {
    return localStorage.getItem(TOKEN_KEY);
  },
  set(token: string) {
    localStorage.setItem(TOKEN_KEY, token);
  },
  clear() {
    localStorage.removeItem(TOKEN_KEY);
  },
};

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('Content-Type', 'application/json');
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);

  const res = await fetch(`${BASE_URL}${path}`, { ...init, headers });
  if (res.status === 401) {
    // Missing/expired session: clear the token and redirect to login (no loop if already there).
    auth.clear();
    if (!window.location.pathname.startsWith('/login')) {
      window.location.assign('/login');
    }
    throw new Error('Sessão expirada. Faça login novamente.');
  }
  if (!res.ok) {
    const contentType = res.headers.get('content-type') ?? '';
    if (contentType.includes('application/problem+json')) {
      try {
        const body = await res.json();
        throw apiErrorFromProblem(res.status, body);
      } catch (e) {
        // Re-throw the ApiError we just built; only fall through if JSON parsing failed.
        if (e instanceof Error && e.name === 'ApiError') throw e;
      }
    }
    const text = await res.text();
    throw new Error(text || `${res.status} ${res.statusText}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// ---------- Wire types (aliases to generated OpenAPI schemas) ----------
// Exported names are kept stable so pages/queries don't need to change.
export type User = Schemas['UserSummary'];
export type Company = Schemas['CompanyResponse'];
export type Unit = Schemas['UnitResponse'];
export type Transaction = Schemas['TransactionResponse'];
export type CostCenter = Schemas['CostCenterResponse'];
export type Address = Schemas['AddressResponse'];

// The DTOs declare `AddressResponse? Address` (nullable), but Swashbuckle can't express the nullability
// of a nested complex member, so it emits `address` as non-null. Restore the honest `Address | null`.
type WithNullableAddress<T extends { address: Address }> = Omit<T, 'address'> & { address: Address | null };
export type Vendor = WithNullableAddress<Schemas['VendorResponse']>;
export type Customer = WithNullableAddress<Schemas['CustomerResponse']>;
export type Employee = WithNullableAddress<Schemas['EmployeeResponse']>;

/**
 * Envelope returned by the paginated browse lists (transactions + registry).
 * Mirrors the backend `PaginatedResponse<T>`; kept as a stable hand-written alias
 * so pages don't depend on the generated, item-type-specific schema names.
 */
export interface Paginated<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

// Enum unions: serialized as strings at runtime via the global JsonStringEnumConverter.
export type TransactionType = Schemas['TransactionType'];
export type TransactionStatus = Schemas['TransactionStatus'];

// ---------- Payables (Contas a Pagar) ----------
export type Payable = Schemas['PayableResponse'];
export type PayableListItem = Schemas['PayableListItemResponse'];
export type Installment = Schemas['InstallmentResponse'];
export type PayableAttachment = Schemas['PayableAttachmentResponse'];
export type ExpenseCategory = Schemas['ExpenseCategoryResponse'];
export type PayableOrigin = Schemas['PayableOrigin'];
export type PayableStatus = Schemas['PayableStatus'];
export type InstallmentStatus = Schemas['InstallmentStatus'];

// ---------- Bank accounts + payments (baixa) ----------
export type BankAccount = Schemas['BankAccountResponse'];
export type Payment = Schemas['PaymentResponse'];
// Enum unions: serialized as strings at runtime via the global JsonStringEnumConverter.
export type BankAccountType = Schemas['BankAccountType'];
export type PaymentMethod = Schemas['PaymentMethod'];

// ---------- Chart of Accounts (Plano de Contas) ----------
export type ChartAccount = Schemas['ChartAccountResponse'];
export type ChartType = Schemas['ChartType'];
export type AccountKind = Schemas['AccountKind'];
export type ClassificationSuggestion = Schemas['ClassificationSuggestionResponse'];
export type ClassificationPolicy = Schemas['ClassificationPolicyResponse'];

// ---------- Conciliação OFX (statement imports + bank transactions) ----------
export type StatementImport = Schemas['StatementImportResponse'];
export type BankTransaction = Schemas['BankTransactionResponse'];
// Enum unions: serialized as strings at runtime via the global JsonStringEnumConverter.
export type MatchStatus = Schemas['MatchStatus'];
export type TransactionDirection = Schemas['TransactionDirection'];

/** Request body for creating a bank account. */
export interface BankAccountInput {
  companyId: string;
  bankCode: string;
  bankName: string;
  agency: string;
  accountNumber: string;
  accountDigit: string;
  accountType: BankAccountType;
  alias: string;
  openingBalance?: number | null;
}

/** Request body for updating a bank account (no companyId; `active` toggles status). */
export type BankAccountUpdateInput = Omit<BankAccountInput, 'companyId'> & { active: boolean };

/** Request body for registering a payment (baixa) against an installment. */
export interface RegisterPaymentInput {
  paymentDate: string;
  amount: number;
  bankAccountId: string;
  method: PaymentMethod;
  interest?: number;
  fine?: number;
  discount?: number;
  notes?: string | null;
}

/** Request body for creating/updating a payable. `installments` lists explicit dues; `generate` derives them. */
export interface PayableInput {
  companyId: string;
  vendorId: string;
  costCenterId: string;
  categoryId?: string | null;
  origin?: PayableOrigin;
  description: string;
  documentNumber?: string | null;
  issueDate?: string | null;
  installments?: { dueDate: string; amount: number }[];
  generate?: { totalAmount: number; count: number; firstDueDate: string; intervalMonths?: number };
  accountingAccountId?: string | null;
  financialAccountId?: string | null;
}

/**
 * Person type. Wire format is the string union <c>'Individual' | 'Company'</c>.
 * Exposed as a type AND a value object so existing call sites (`PersonType.Company`,
 * `useState<PersonType>`, equality checks) keep working without churn.
 */
export type PersonType = Schemas['PersonType'];
export const PersonType = {
  Individual: 'Individual',
  Company: 'Company',
} as const satisfies Record<string, PersonType>;

// Request bodies for the registry create/update methods. Derived from the response shapes (minus
// server-owned fields) rather than the generated *Request schemas: Swashbuckle can't reliably express
// the nullability of nested record members (e.g. `AddressRequest? Address`), so the generated request
// types would force a non-null address. These Omit-derived types match what the forms actually send.
export type VendorInput = Omit<Vendor, 'id' | 'active'>;
export type CustomerInput = Omit<Customer, 'id' | 'active'>;
export type EmployeeInput = Omit<Employee, 'id' | 'active'>;

export type CepLookup = Schemas['CepResult'];
export type CnpjLookup = Schemas['CnpjResult'];

/** Builds list-query string shared by registry resources. */
function registryQuery(companyId: string, search?: string, page?: number, pageSize?: number) {
  const q = new URLSearchParams({ companyId });
  if (search?.trim()) q.set('search', search.trim());
  if (page) q.set('page', String(page));
  if (pageSize) q.set('pageSize', String(pageSize));
  return q.toString();
}

export const api = {
  devLogin: (email: string, name: string) =>
    request<Schemas['LoginResponse']>('/auth/dev-token', {
      method: 'POST',
      body: JSON.stringify({ email, name }),
    }),
  me: () => request<Schemas['MeResponse']>('/auth/me'),

  listCompanies: () => request<Company[]>('/companies'),
  createCompany: (legalName: string, cnpj: string) =>
    request<Company>('/companies', { method: 'POST', body: JSON.stringify({ legalName, cnpj }) }),
  updateCompany: (id: string, legalName: string, cnpj: string) =>
    request<Company>(`/companies/${id}`, { method: 'PUT', body: JSON.stringify({ legalName, cnpj }) }),

  listUnits: (companyId: string) => request<Unit[]>(`/units?companyId=${companyId}`),
  createUnit: (companyId: string, name: string, acronym: string) =>
    request<Unit>('/units', { method: 'POST', body: JSON.stringify({ companyId, name, acronym }) }),
  updateUnit: (id: string, name: string, acronym: string) =>
    request<Unit>(`/units/${id}`, { method: 'PUT', body: JSON.stringify({ name, acronym }) }),

  listCostCenters: (companyId: string) => request<CostCenter[]>(`/cost-centers?companyId=${companyId}`),
  getCostCenter: (id: string) => request<CostCenter>(`/cost-centers/${id}`),
  createCostCenter: (
    companyId: string,
    code: string,
    name: string,
    annualBudget: number,
    autoApprovalLimit: number,
    unitIds: string[],
  ) =>
    request<CostCenter>('/cost-centers', {
      method: 'POST',
      body: JSON.stringify({ companyId, code, name, annualBudget, autoApprovalLimit, unitIds }),
    }),
  updateCostCenter: (
    id: string,
    code: string,
    name: string,
    annualBudget: number,
    autoApprovalLimit: number,
    unitIds: string[],
  ) =>
    request<CostCenter>(`/cost-centers/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ code, name, annualBudget, autoApprovalLimit, unitIds }),
    }),

  listTransactions: (params: {
    companyId?: string;
    type?: TransactionType;
    status?: TransactionStatus;
    costCenterId?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.type) q.set('type', params.type);
    if (params.status) q.set('status', params.status);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<Transaction>>(`/transactions?${q}`);
  },
  getTransaction: (id: string) => request<Transaction>(`/transactions/${id}`),
  createTransaction: (body: {
    companyId: string;
    unitId: string;
    costCenterId: string;
    type: TransactionType;
    description: string;
    vendorId?: string | null;
    customerId?: string | null;
    amount: number;
    dueDate: string;
  }) => request<Transaction>('/transactions', { method: 'POST', body: JSON.stringify(body) }),
  approveTransaction: (id: string) => request<Transaction>(`/transactions/${id}/approve`, { method: 'POST' }),
  rejectTransaction: (id: string, reason: string) =>
    request<Transaction>(`/transactions/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) }),
  payTransaction: (id: string) => request<Transaction>(`/transactions/${id}/pay`, { method: 'POST' }),

  // ---------- Payables (Contas a Pagar) ----------
  listPayables: (params: {
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
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.status) q.set('status', params.status);
    if (params.vendorId) q.set('vendorId', params.vendorId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.categoryId) q.set('categoryId', params.categoryId);
    if (params.dueFrom) q.set('dueFrom', params.dueFrom);
    if (params.dueTo) q.set('dueTo', params.dueTo);
    if (params.paid !== undefined) q.set('paid', String(params.paid));
    if (params.classified !== undefined) q.set('classified', String(params.classified));
    if (params.page) q.set('Page', String(params.page));
    if (params.pageSize) q.set('PageSize', String(params.pageSize));
    return request<Paginated<PayableListItem>>(`/payables?${q}`);
  },
  getPayable: (id: string) => request<Payable>(`/payables/${id}`),
  createPayable: (body: PayableInput) => request<Payable>('/payables', { method: 'POST', body: JSON.stringify(body) }),
  updatePayable: (id: string, body: Omit<PayableInput, 'origin'>) =>
    request<Payable>(`/payables/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  approvePayable: (id: string) => request<Payable>(`/payables/${id}/approve`, { method: 'POST' }),
  rejectPayable: (id: string, reason: string) =>
    request<Payable>(`/payables/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) }),
  cancelPayable: (id: string, reason: string) =>
    request<Payable>(`/payables/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),
  /** Uploads an attachment via multipart/form-data. Uses fetch directly so the browser sets the boundary. */
  uploadPayableAttachment: async (id: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/payables/${id}/attachments`, { method: 'POST', headers, body: form });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<PayableAttachment>;
  },
  /** Direct download URL for an attachment (auth handled by the browser session / token in caller). */
  downloadPayableAttachmentUrl: (id: string, attachmentId: string) =>
    `${BASE_URL}/payables/${id}/attachments/${attachmentId}`,
  deletePayableAttachment: (id: string, attachmentId: string) =>
    request<void>(`/payables/${id}/attachments/${attachmentId}`, { method: 'DELETE' }),

  // ---------- Payments (baixa) ----------
  registerPayment: (payableId: string, installmentId: string, body: RegisterPaymentInput) =>
    request<Payable>(`/payables/${payableId}/installments/${installmentId}/payments`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  reversePayment: (payableId: string, paymentId: string, reason: string) =>
    request<Payable>(`/payables/${payableId}/payments/${paymentId}/reverse`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),
  /** Uploads a payment proof via multipart/form-data. Uses fetch directly so the browser sets the boundary. */
  uploadPaymentProof: async (payableId: string, paymentId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/payables/${payableId}/payments/${paymentId}/proof`, {
      method: 'POST',
      headers,
      body: form,
    });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<Payment>;
  },
  /** Direct download URL for a payment proof (auth handled by the browser session / token in caller). */
  downloadPaymentProofUrl: (payableId: string, paymentId: string) =>
    `${BASE_URL}/payables/${payableId}/payments/${paymentId}/proof`,

  // ---------- Bank accounts ----------
  listBankAccounts: (companyId: string, includeInactive?: boolean) => {
    const q = new URLSearchParams({ companyId });
    if (includeInactive) q.set('includeInactive', 'true');
    return request<BankAccount[]>(`/bank-accounts?${q}`);
  },
  createBankAccount: (body: BankAccountInput) =>
    request<BankAccount>('/bank-accounts', { method: 'POST', body: JSON.stringify(body) }),
  updateBankAccount: (id: string, body: BankAccountUpdateInput) =>
    request<BankAccount>(`/bank-accounts/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteBankAccount: (id: string) => request<void>(`/bank-accounts/${id}`, { method: 'DELETE' }),

  // ---------- Conciliação OFX (statement imports + bank transactions) ----------
  /** Imports an OFX statement via multipart/form-data. Uses fetch directly so the browser sets the boundary. */
  importStatement: async (bankAccountId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/statement-imports?bankAccountId=${bankAccountId}`, {
      method: 'POST',
      headers,
      body: form,
    });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<StatementImport>;
  },
  listStatementImports: (companyId?: string, bankAccountId?: string) => {
    const q = new URLSearchParams();
    if (companyId) q.set('companyId', companyId);
    if (bankAccountId) q.set('bankAccountId', bankAccountId);
    return request<StatementImport[]>(`/statement-imports?${q}`);
  },
  listBankTransactions: (params: {
    companyId?: string;
    bankAccountId?: string;
    importId?: string;
    matchStatus?: MatchStatus;
    dueFrom?: string;
    dueTo?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.bankAccountId) q.set('bankAccountId', params.bankAccountId);
    if (params.importId) q.set('importId', params.importId);
    if (params.matchStatus) q.set('matchStatus', params.matchStatus);
    if (params.dueFrom) q.set('dueFrom', params.dueFrom);
    if (params.dueTo) q.set('dueTo', params.dueTo);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<BankTransaction>>(`/bank-transactions?${q}`);
  },
  confirmMatch: (id: string) => request<BankTransaction>(`/bank-transactions/${id}/confirm`, { method: 'POST' }),
  matchTransaction: (id: string, installmentId: string) =>
    request<BankTransaction>(`/bank-transactions/${id}/match`, {
      method: 'POST',
      body: JSON.stringify({ installmentId }),
    }),
  ignoreTransaction: (id: string) => request<BankTransaction>(`/bank-transactions/${id}/ignore`, { method: 'POST' }),
  unmatchTransaction: (id: string) => request<BankTransaction>(`/bank-transactions/${id}/unmatch`, { method: 'POST' }),

  // ---------- Chart of Accounts (Plano de Contas) ----------
  listChartAccounts: (params: { chartType?: ChartType; kind?: AccountKind; activeOnly?: boolean }) => {
    const q = new URLSearchParams();
    if (params.chartType) q.set('chartType', params.chartType);
    if (params.kind) q.set('kind', params.kind);
    if (params.activeOnly != null) q.set('activeOnly', String(params.activeOnly));
    return request<ChartAccount[]>(`/chart-accounts?${q}`);
  },
  createChartAccount: (body: {
    chartType: ChartType;
    code: string;
    name: string;
    parentId?: string | null;
    kind: AccountKind;
    nature: Schemas['AccountNature'] | null;
  }) => request<ChartAccount>('/chart-accounts', { method: 'POST', body: JSON.stringify(body) }),
  updateChartAccount: (
    id: string,
    body: {
      code?: string;
      name?: string;
      parentId?: string | null;
      kind?: AccountKind;
      nature?: Schemas['AccountNature'] | null;
      isActive?: boolean;
    },
  ) => request<ChartAccount>(`/chart-accounts/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteChartAccount: (id: string) => request<void>(`/chart-accounts/${id}`, { method: 'DELETE' }),

  // ---------- Classification ----------
  classifyPayable: (id: string, body: { accountingAccountId?: string | null; financialAccountId?: string | null }) =>
    request<Payable>(`/payables/${id}/classify`, { method: 'POST', body: JSON.stringify(body) }),
  classifyBatch: (body: {
    payableIds: string[];
    accountingAccountId?: string | null;
    financialAccountId?: string | null;
  }) =>
    request<{ classifiedCount: number }>('/payables/classify-batch', { method: 'POST', body: JSON.stringify(body) }),
  classificationSuggestion: (payableId: string) =>
    request<ClassificationSuggestion>(`/payables/${payableId}/classification-suggestion`),

  // ---------- Classification Policies ----------
  getClassificationPolicy: (companyId: string) =>
    request<ClassificationPolicy>(`/classification-policies/${companyId}`),
  updateClassificationPolicy: (companyId: string, body: { requireToApprove: boolean; requireToPay: boolean }) =>
    request<ClassificationPolicy>(`/classification-policies/${companyId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  // ---------- Expense categories ----------
  listExpenseCategories: (includeInactive?: boolean) =>
    request<ExpenseCategory[]>(`/expense-categories${includeInactive ? '?includeInactive=true' : ''}`),
  createExpenseCategory: (name: string) =>
    request<ExpenseCategory>('/expense-categories', { method: 'POST', body: JSON.stringify({ name }) }),
  updateExpenseCategory: (id: string, name: string, active: boolean) =>
    request<ExpenseCategory>(`/expense-categories/${id}`, { method: 'PUT', body: JSON.stringify({ name, active }) }),
  deleteExpenseCategory: (id: string) => request<void>(`/expense-categories/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Vendors ----------
  listVendors: (companyId: string, search?: string, page?: number, pageSize?: number) =>
    request<Paginated<Vendor>>(`/vendors?${registryQuery(companyId, search, page, pageSize)}`),
  getVendor: (id: string) => request<Vendor>(`/vendors/${id}`),
  createVendor: (body: VendorInput) => request<Vendor>('/vendors', { method: 'POST', body: JSON.stringify(body) }),
  updateVendor: (id: string, body: VendorInput) =>
    request<Vendor>(`/vendors/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateVendor: (id: string) => request<Vendor>(`/vendors/${id}/deactivate`, { method: 'POST' }),
  activateVendor: (id: string) => request<Vendor>(`/vendors/${id}/activate`, { method: 'POST' }),
  removeVendor: (id: string) => request<void>(`/vendors/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Customers ----------
  listCustomers: (companyId: string, search?: string, page?: number, pageSize?: number) =>
    request<Paginated<Customer>>(`/customers?${registryQuery(companyId, search, page, pageSize)}`),
  getCustomer: (id: string) => request<Customer>(`/customers/${id}`),
  createCustomer: (body: CustomerInput) =>
    request<Customer>('/customers', { method: 'POST', body: JSON.stringify(body) }),
  updateCustomer: (id: string, body: CustomerInput) =>
    request<Customer>(`/customers/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateCustomer: (id: string) => request<Customer>(`/customers/${id}/deactivate`, { method: 'POST' }),
  activateCustomer: (id: string) => request<Customer>(`/customers/${id}/activate`, { method: 'POST' }),
  removeCustomer: (id: string) => request<void>(`/customers/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Employees ----------
  listEmployees: (companyId: string, search?: string, page?: number, pageSize?: number) =>
    request<Paginated<Employee>>(`/employees?${registryQuery(companyId, search, page, pageSize)}`),
  getEmployee: (id: string) => request<Employee>(`/employees/${id}`),
  createEmployee: (body: EmployeeInput) =>
    request<Employee>('/employees', { method: 'POST', body: JSON.stringify(body) }),
  updateEmployee: (id: string, body: EmployeeInput) =>
    request<Employee>(`/employees/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateEmployee: (id: string) => request<Employee>(`/employees/${id}/deactivate`, { method: 'POST' }),
  activateEmployee: (id: string) => request<Employee>(`/employees/${id}/activate`, { method: 'POST' }),
  removeEmployee: (id: string) => request<void>(`/employees/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Lookups ----------
  lookupCep: (cep: string) => request<CepLookup>(`/lookup/cep/${cep}`),
  lookupCnpj: (cnpj: string) => request<CnpjLookup>(`/lookup/cnpj/${cnpj}`),
};
