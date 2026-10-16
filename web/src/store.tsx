import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { type Company } from './api';
import { ALL_COMPANIES, queryKeys, useCompanies, useMe } from './queries';
import { canSeeConsolidated } from './permissions';

// Re-export so consumers can import the sentinel from the store (defined in queries.ts to avoid a cycle).
export { ALL_COMPANIES };

interface AppCtx {
  empresas: Company[];
  empresaId: string;
  empresa: Company | null;
  /** True when the consolidated "Todas as empresas" mode is active (empresaId === ALL_COMPANIES). */
  isAllCompanies: boolean;
  /** Real company id to send to the API, or undefined in consolidated mode (omit the companyId filter). */
  scopeCompanyId: string | undefined;
  /** A scope is resolved — a real company OR "Todas". Screens gate their render on this, not on `empresa`. */
  scopeReady: boolean;
  /** Whether this user may use the consolidated view (drives the topbar "Todas" option). */
  canSeeAllCompanies: boolean;
  setEmpresaId: (id: string) => void;
  refreshEmpresas: (selecionar?: string) => Promise<void>;
}

const Ctx = createContext<AppCtx>(null!);
export const useApp = () => useContext(Ctx);

// Persisted so the company selection (now driven from the topbar) survives reloads.
const EMPRESA_KEY = 'ulbra-one.empresaId';

export function AppDataProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const { data } = useCompanies();
  const companiesLoaded = data !== undefined;
  const me = useMe().data;
  const canSeeAllCompanies = canSeeConsolidated(me);
  // Consolidated roles (AEL-906) see every company; scoped/User see only their granted ones.
  const empresas = useMemo(() => {
    const all = data ?? [];
    if (!me || canSeeAllCompanies) return all;
    return all.filter((c) => (me.companyIds ?? []).includes(c.id));
  }, [data, me, canSeeAllCompanies]);
  const [empresaId, setEmpresaId] = useState<string>(() => {
    try {
      return window.localStorage.getItem(EMPRESA_KEY) ?? '';
    } catch {
      return '';
    }
  });

  // Normalize the selection: keep a still-valid choice; else consolidated roles default to "Todas", others to
  // the first company. Waits for BOTH `me` and the companies list: normalizing before the list loads would
  // judge a stored company against an empty list and wrongly clobber it to the "Todas" sentinel (reload race).
  useEffect(() => {
    if (me === undefined || !companiesLoaded) return;
    setEmpresaId((cur) => {
      if (cur === ALL_COMPANIES) return canSeeAllCompanies ? ALL_COMPANIES : (empresas[0]?.id ?? '');
      if (cur && empresas.some((e) => e.id === cur)) return cur;
      return canSeeAllCompanies ? ALL_COMPANIES : (empresas[0]?.id ?? '');
    });
  }, [empresas, me, canSeeAllCompanies, companiesLoaded]);

  // Persist the current selection (incl. the ALL_COMPANIES sentinel) so a reload restores it.
  useEffect(() => {
    try {
      if (empresaId) window.localStorage.setItem(EMPRESA_KEY, empresaId);
    } catch {
      // Private mode / storage disabled: selection still works for this session.
    }
  }, [empresaId]);

  const refreshEmpresas = async (selecionar?: string) => {
    // refetch (not just invalidate) so the fresh list is in cache before we pick a selection.
    await qc.refetchQueries({ queryKey: queryKeys.companies });
    const es = qc.getQueryData<Company[]>(queryKeys.companies) ?? empresas;
    setEmpresaId((cur) => {
      const want = selecionar ?? cur;
      if (want === ALL_COMPANIES && canSeeAllCompanies) return ALL_COMPANIES;
      return es.find((e) => e.id === want) ? want : (es[0]?.id ?? '');
    });
  };

  const isAllCompanies = empresaId === ALL_COMPANIES;
  const empresa = isAllCompanies ? null : (empresas.find((e) => e.id === empresaId) ?? null);

  const value: AppCtx = {
    empresas,
    empresaId,
    empresa,
    isAllCompanies,
    scopeCompanyId: isAllCompanies ? undefined : empresaId || undefined,
    scopeReady: isAllCompanies || !!empresa,
    canSeeAllCompanies,
    setEmpresaId,
    refreshEmpresas,
  };
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
