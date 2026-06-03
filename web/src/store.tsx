import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, type Company } from './api';

interface AppCtx {
  empresas: Company[];
  empresaId: string;
  empresa: Company | null;
  setEmpresaId: (id: string) => void;
  refreshEmpresas: (selecionar?: string) => Promise<void>;
}

const Ctx = createContext<AppCtx>(null!);
export const useApp = () => useContext(Ctx);

export function AppDataProvider({ children }: { children: ReactNode }) {
  const [empresas, setEmpresas] = useState<Company[]>([]);
  const [empresaId, setEmpresaId] = useState('');

  const refreshEmpresas = async (selecionar?: string) => {
    const es = await api.listCompanies();
    setEmpresas(es);
    setEmpresaId((cur) => {
      const want = selecionar ?? cur;
      return es.find((e) => e.id === want) ? want : (es[0]?.id ?? '');
    });
  };

  useEffect(() => { refreshEmpresas().catch(() => {}); }, []);

  const value: AppCtx = {
    empresas, empresaId, empresa: empresas.find((e) => e.id === empresaId) ?? null,
    setEmpresaId, refreshEmpresas,
  };
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
