import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { type Company } from './api';
import { queryKeys, useCompanies } from './queries';

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
  const qc = useQueryClient();
  const { data } = useCompanies();
  const empresas = data ?? [];
  const [empresaId, setEmpresaId] = useState('');

  // Keep a valid selection (auto-select first) whenever the company list changes.
  useEffect(() => {
    setEmpresaId((cur) => (empresas.find((e) => e.id === cur) ? cur : (empresas[0]?.id ?? '')));
  }, [empresas]);

  const refreshEmpresas = async (selecionar?: string) => {
    // refetch (not just invalidate) so the fresh list is in cache before we pick a selection.
    await qc.refetchQueries({ queryKey: queryKeys.companies });
    const es = qc.getQueryData<Company[]>(queryKeys.companies) ?? empresas;
    setEmpresaId((cur) => {
      const want = selecionar ?? cur;
      return es.find((e) => e.id === want) ? want : (es[0]?.id ?? '');
    });
  };

  const value: AppCtx = {
    empresas,
    empresaId,
    empresa: empresas.find((e) => e.id === empresaId) ?? null,
    setEmpresaId,
    refreshEmpresas,
  };
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
