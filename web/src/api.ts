import { apiErrorFromProblem } from './apiError';
import type { components } from './api/schema';

/** All generated OpenAPI component schemas. Source of truth for wire types. */
type Schemas = components['schemas'];

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5001/api';

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
    counterparty: string;
    amount: number;
    dueDate: string;
  }) => request<Transaction>('/transactions', { method: 'POST', body: JSON.stringify(body) }),
  approveTransaction: (id: string) => request<Transaction>(`/transactions/${id}/approve`, { method: 'POST' }),
  rejectTransaction: (id: string, reason: string) =>
    request<Transaction>(`/transactions/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) }),
  payTransaction: (id: string) => request<Transaction>(`/transactions/${id}/pay`, { method: 'POST' }),

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
