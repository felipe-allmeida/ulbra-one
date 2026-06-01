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

export interface Usuario {
  id: string;
  email: string;
  nome: string;
  role: string;
}

export interface Empresa {
  id: string;
  razaoSocial: string;
  cnpj: string;
  ativa: boolean;
}

export interface Unidade {
  id: string;
  empresaId: string;
  nome: string;
  sigla: string;
  ativa: boolean;
}

export type TipoLancamento = 'Pagar' | 'Receber';
export type StatusLancamento = 'Pendente' | 'Aprovado' | 'Pago' | 'Rejeitado';

export interface Lancamento {
  id: string;
  empresaId: string;
  unidadeId: string;
  centroDeCustoId: string;
  tipo: TipoLancamento;
  descricao: string;
  contraparte: string;
  valor: number;
  vencimento: string;
  status: StatusLancamento;
  motivoRejeicao: string | null;
  criadoEm: string;
  aprovadoEm: string | null;
  pagoEm: string | null;
}

export interface CentroDeCusto {
  id: string;
  empresaId: string;
  codigo: string;
  nome: string;
  orcamentoAnual: number;
  limiteAutoAprovacao: number;
  ativo: boolean;
  unidadeIds: string[];
}

export const api = {
  devLogin: (email: string, nome: string) =>
    request<{ token: string; usuario: Usuario }>('/auth/dev-token', {
      method: 'POST',
      body: JSON.stringify({ email, nome }),
    }),
  me: () => request<Usuario & { isAdmin: boolean }>('/auth/me'),

  listEmpresas: () => request<Empresa[]>('/empresas'),
  createEmpresa: (razaoSocial: string, cnpj: string) =>
    request<Empresa>('/empresas', { method: 'POST', body: JSON.stringify({ razaoSocial, cnpj }) }),
  updateEmpresa: (id: string, razaoSocial: string, cnpj: string) =>
    request<Empresa>(`/empresas/${id}`, { method: 'PUT', body: JSON.stringify({ razaoSocial, cnpj }) }),

  listUnidades: (empresaId: string) => request<Unidade[]>(`/unidades?empresaId=${empresaId}`),
  createUnidade: (empresaId: string, nome: string, sigla: string) =>
    request<Unidade>('/unidades', { method: 'POST', body: JSON.stringify({ empresaId, nome, sigla }) }),
  updateUnidade: (id: string, nome: string, sigla: string) =>
    request<Unidade>(`/unidades/${id}`, { method: 'PUT', body: JSON.stringify({ nome, sigla }) }),

  listCentros: (empresaId: string) => request<CentroDeCusto[]>(`/centros-custo?empresaId=${empresaId}`),
  getCentro: (id: string) => request<CentroDeCusto>(`/centros-custo/${id}`),
  createCentro: (empresaId: string, codigo: string, nome: string, orcamentoAnual: number, limiteAutoAprovacao: number, unidadeIds: string[]) =>
    request<CentroDeCusto>('/centros-custo', {
      method: 'POST',
      body: JSON.stringify({ empresaId, codigo, nome, orcamentoAnual, limiteAutoAprovacao, unidadeIds }),
    }),
  updateCentro: (id: string, codigo: string, nome: string, orcamentoAnual: number, limiteAutoAprovacao: number, unidadeIds: string[]) =>
    request<CentroDeCusto>(`/centros-custo/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ codigo, nome, orcamentoAnual, limiteAutoAprovacao, unidadeIds }),
    }),

  listLancamentos: (params: { empresaId?: string; tipo?: TipoLancamento; status?: StatusLancamento; centroDeCustoId?: string }) => {
    const q = new URLSearchParams();
    if (params.empresaId) q.set('empresaId', params.empresaId);
    if (params.tipo) q.set('tipo', params.tipo);
    if (params.status) q.set('status', params.status);
    if (params.centroDeCustoId) q.set('centroDeCustoId', params.centroDeCustoId);
    return request<Lancamento[]>(`/lancamentos?${q}`);
  },
  getLancamento: (id: string) => request<Lancamento>(`/lancamentos/${id}`),
  createLancamento: (body: {
    empresaId: string; unidadeId: string; centroDeCustoId: string; tipo: TipoLancamento;
    descricao: string; contraparte: string; valor: number; vencimento: string;
  }) => request<Lancamento>('/lancamentos', { method: 'POST', body: JSON.stringify(body) }),
  aprovarLancamento: (id: string) => request<Lancamento>(`/lancamentos/${id}/aprovar`, { method: 'POST' }),
  rejeitarLancamento: (id: string, motivo: string) => request<Lancamento>(`/lancamentos/${id}/rejeitar`, { method: 'POST', body: JSON.stringify({ motivo }) }),
  pagarLancamento: (id: string) => request<Lancamento>(`/lancamentos/${id}/pagar`, { method: 'POST' }),
};
