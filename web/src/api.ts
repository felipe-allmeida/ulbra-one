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
    // Sessão ausente/expirada: limpa o token e manda pro login (sem loop se já estamos lá).
    auth.clear();
    if (!window.location.pathname.startsWith('/login')) {
      window.location.assign('/login');
    }
    throw new Error('Sessão expirada. Faça login novamente.');
  }
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `${res.status} ${res.statusText}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export interface User {
  id: string;
  email: string;
  name: string;
  role: string;
}

export interface Company {
  id: string;
  legalName: string;
  cnpj: string;
  active: boolean;
}

export interface Unit {
  id: string;
  companyId: string;
  name: string;
  acronym: string;
  active: boolean;
}

export type TransactionType = 'Payable' | 'Receivable';
export type TransactionStatus = 'Pending' | 'Approved' | 'Paid' | 'Rejected';

export interface Transaction {
  id: string;
  companyId: string;
  unitId: string;
  costCenterId: string;
  type: TransactionType;
  description: string;
  counterparty: string;
  amount: number;
  dueDate: string;
  status: TransactionStatus;
  rejectionReason: string | null;
  createdAt: string;
  approvedAt: string | null;
  paidAt: string | null;
}

export interface CostCenter {
  id: string;
  companyId: string;
  code: string;
  name: string;
  annualBudget: number;
  autoApprovalLimit: number;
  active: boolean;
  unitIds: string[];
}

/** 0 = Individual (PF), 1 = Company (PJ). */
export enum PersonType {
  Individual = 0,
  Company = 1,
}

export interface Address {
  cep: string;
  street: string;
  number: string;
  complement: string | null;
  neighborhood: string;
  city: string;
  state: string;
}

export interface Vendor {
  id: string;
  companyId: string;
  personType: PersonType;
  name: string;
  tradeName: string | null;
  document: string;
  email: string | null;
  phone: string | null;
  address: Address | null;
  paymentTerms: string | null;
  bankInfo: string | null;
  active: boolean;
}

export interface Customer {
  id: string;
  companyId: string;
  personType: PersonType;
  name: string;
  tradeName: string | null;
  document: string;
  email: string | null;
  phone: string | null;
  address: Address | null;
  creditLimit: number | null;
  active: boolean;
}

export interface Employee {
  id: string;
  companyId: string;
  name: string;
  document: string;
  email: string | null;
  phone: string | null;
  role: string;
  unitId: string;
  costCenterId: string | null;
  hireDate: string;
  address: Address | null;
  active: boolean;
}

export type VendorInput = Omit<Vendor, 'id' | 'active'>;
export type CustomerInput = Omit<Customer, 'id' | 'active'>;
export type EmployeeInput = Omit<Employee, 'id' | 'active'>;

export interface CepLookup {
  cep: string;
  street: string;
  neighborhood: string;
  city: string;
  state: string;
}
export interface CnpjLookup {
  razaoSocial: string;
  nomeFantasia: string | null;
  address: { cep: string; street: string; number: string; neighborhood: string; city: string; state: string } | null;
}

/** Builds list-query string shared by registry resources. */
function registryQuery(companyId: string, search?: string) {
  const q = new URLSearchParams({ companyId });
  if (search?.trim()) q.set('search', search.trim());
  return q.toString();
}

export const api = {
  devLogin: (email: string, name: string) =>
    request<{ token: string; user: User }>('/auth/dev-token', {
      method: 'POST',
      body: JSON.stringify({ email, name }),
    }),
  me: () => request<{ userId: string; name: string; email: string; role: string; isAdmin: boolean }>('/auth/me'),

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
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.type) q.set('type', params.type);
    if (params.status) q.set('status', params.status);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    return request<Transaction[]>(`/transactions?${q}`);
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
  listVendors: (companyId: string, search?: string) =>
    request<Vendor[]>(`/vendors?${registryQuery(companyId, search)}`),
  getVendor: (id: string) => request<Vendor>(`/vendors/${id}`),
  createVendor: (body: VendorInput) => request<Vendor>('/vendors', { method: 'POST', body: JSON.stringify(body) }),
  updateVendor: (id: string, body: VendorInput) =>
    request<Vendor>(`/vendors/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateVendor: (id: string) => request<Vendor>(`/vendors/${id}/deactivate`, { method: 'POST' }),
  activateVendor: (id: string) => request<Vendor>(`/vendors/${id}/activate`, { method: 'POST' }),
  removeVendor: (id: string) => request<void>(`/vendors/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Customers ----------
  listCustomers: (companyId: string, search?: string) =>
    request<Customer[]>(`/customers?${registryQuery(companyId, search)}`),
  getCustomer: (id: string) => request<Customer>(`/customers/${id}`),
  createCustomer: (body: CustomerInput) =>
    request<Customer>('/customers', { method: 'POST', body: JSON.stringify(body) }),
  updateCustomer: (id: string, body: CustomerInput) =>
    request<Customer>(`/customers/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateCustomer: (id: string) => request<Customer>(`/customers/${id}/deactivate`, { method: 'POST' }),
  activateCustomer: (id: string) => request<Customer>(`/customers/${id}/activate`, { method: 'POST' }),
  removeCustomer: (id: string) => request<void>(`/customers/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Employees ----------
  listEmployees: (companyId: string, search?: string) =>
    request<Employee[]>(`/employees?${registryQuery(companyId, search)}`),
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
