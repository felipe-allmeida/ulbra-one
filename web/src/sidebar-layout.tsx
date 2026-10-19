import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

/**
 * Preferência de layout da navegação lateral no desktop: recolhida ou não, e a largura do painel.
 *
 * Recolhida NÃO é largura zero — o rail de 82px fica, com os ícones de domínio, então trocar de
 * domínio continua possível sem expandir. É esse estado intermediário que devolve 225px ao conteúdo
 * sem custar a navegação.
 *
 * É provider (e não um hook solto) porque o estado tem dois consumidores em pontas opostas do
 * chrome: o gatilho de recolher vive na topbar, à esquerda — onde Linear, Azure e o Claude Desktop
 * o põem — e a alça de redimensionar vive na borda do próprio painel. Dois `useState` separados
 * dessincronizariam na primeira interação.
 *
 * Só vale acima de 1024px. Abaixo disso a navegação é drawer (641–1024) ou folha de duas camadas
 * (≤640), e nas duas o painel precisa aparecer inteiro — quem neutraliza a preferência lá é o CSS
 * (`@media (max-width: 1024px)` em index.css), não este hook: assim o breakpoint continua existindo
 * num lugar só, em vez de ser duplicado em JS como os 640px de `usePhone()` já são.
 */

/** Os 225px que a sidebar sempre teve — sem preferência salva, nada muda de aparência. */
export const NAV_PANEL_DEFAULT = 225;
/** Abaixo disto os rótulos dos itens truncam a ponto de não servirem. */
export const NAV_PANEL_MIN = 180;
/** Acima disto vira desperdício de tela num app cujo conteúdo é tabela. */
export const NAV_PANEL_MAX = 420;

const COLLAPSED_KEY = 'ulbra-one.nav.collapsed';
const WIDTH_KEY = 'ulbra-one.nav.width';

function readCollapsed(): boolean {
  try {
    return window.localStorage.getItem(COLLAPSED_KEY) === 'true';
  } catch {
    return false;
  }
}

function readWidth(): number {
  try {
    const stored = Number(window.localStorage.getItem(WIDTH_KEY));
    // Fora da faixa ou não-numérico cai no padrão: um valor corrompido não deve virar uma sidebar
    // de 9999px que esconde o app inteiro.
    if (!Number.isFinite(stored) || stored < NAV_PANEL_MIN || stored > NAV_PANEL_MAX) return NAV_PANEL_DEFAULT;
    return stored;
  } catch {
    return NAV_PANEL_DEFAULT;
  }
}

export interface SidebarLayout {
  collapsed: boolean;
  width: number;
  toggle: () => void;
  /** Fecha um gesto de arrasto ou um passo de teclado. Abaixo do mínimo recolhe em vez de travar
   *  na borda — é o gesto que as pessoas tentam, e a largura anterior fica de pé para o expandir. */
  commitWidth: (next: number) => void;
}

const SidebarLayoutContext = createContext<SidebarLayout | null>(null);

export function SidebarLayoutProvider({ children }: { children: ReactNode }) {
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const [width, setWidth] = useState(readWidth);

  useEffect(() => {
    try {
      window.localStorage.setItem(COLLAPSED_KEY, String(collapsed));
      window.localStorage.setItem(WIDTH_KEY, String(width));
    } catch {
      // Modo privado / armazenamento bloqueado: a preferência vale nesta sessão e não persiste.
    }
  }, [collapsed, width]);

  const commitWidth = useCallback((next: number) => {
    if (next < NAV_PANEL_MIN) {
      setCollapsed(true);
      return;
    }
    setCollapsed(false);
    setWidth(Math.min(next, NAV_PANEL_MAX));
  }, []);

  const toggle = useCallback(() => setCollapsed((c) => !c), []);

  const value = useMemo(() => ({ collapsed, width, toggle, commitWidth }), [collapsed, width, toggle, commitWidth]);

  return <SidebarLayoutContext.Provider value={value}>{children}</SidebarLayoutContext.Provider>;
}

export function useSidebarLayout(): SidebarLayout {
  const ctx = useContext(SidebarLayoutContext);
  if (!ctx) throw new Error('useSidebarLayout must be used within a SidebarLayoutProvider');
  return ctx;
}
