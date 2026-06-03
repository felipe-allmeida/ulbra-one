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

export const api = {
  devLogin: (email: string, name: string) =>
    request<{ token: string; user: User }>('/auth/dev-token', {
      method: 'POST', body: JSON.stringify({ email, name }),
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
  createCostCenter: (companyId: string, code: string, name: string, annualBudget: number, autoApprovalLimit: number, unitIds: string[]) =>
    request<CostCenter>('/cost-centers', { method: 'POST', body: JSON.stringify({ companyId, code, name, annualBudget, autoApprovalLimit, unitIds }) }),
  updateCostCenter: (id: string, code: string, name: string, annualBudget: number, autoApprovalLimit: number, unitIds: string[]) =>
    request<CostCenter>(`/cost-centers/${id}`, { method: 'PUT', body: JSON.stringify({ code, name, annualBudget, autoApprovalLimit, unitIds }) }),

  listTransactions: (params: { companyId?: string; type?: TransactionType; status?: TransactionStatus; costCenterId?: string }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.type) q.set('type', params.type);
    if (params.status) q.set('status', params.status);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    return request<Transaction[]>(`/transactions?${q}`);
  },
  getTransaction: (id: string) => request<Transaction>(`/transactions/${id}`),
  createTransaction: (body: {
    companyId: string; unitId: string; costCenterId: string; type: TransactionType;
    description: string; counterparty: string; amount: number; dueDate: string;
  }) => request<Transaction>('/transactions', { method: 'POST', body: JSON.stringify(body) }),
  approveTransaction: (id: string) => request<Transaction>(`/transactions/${id}/approve`, { method: 'POST' }),
  rejectTransaction: (id: string, reason: string) => request<Transaction>(`/transactions/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) }),
  payTransaction: (id: string) => request<Transaction>(`/transactions/${id}/pay`, { method: 'POST' }),
};
