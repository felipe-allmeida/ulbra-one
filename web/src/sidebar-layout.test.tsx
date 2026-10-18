import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import {
  SidebarLayoutProvider,
  useSidebarLayout,
  NAV_PANEL_DEFAULT,
  NAV_PANEL_MIN,
  NAV_PANEL_MAX,
} from './sidebar-layout';

import type { ReactNode } from 'react';

const wrapper = ({ children }: { children: ReactNode }) => <SidebarLayoutProvider>{children}</SidebarLayoutProvider>;

const COLLAPSED_KEY = 'ulbra-one.nav.collapsed';
const WIDTH_KEY = 'ulbra-one.nav.width';

beforeEach(() => window.localStorage.clear());
afterEach(() => vi.restoreAllMocks());

describe('useSidebarLayout — padrão', () => {
  it('sem preferência salva, abre expandido nos 225px de hoje', () => {
    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    expect(result.current.collapsed).toBe(false);
    expect(result.current.width).toBe(NAV_PANEL_DEFAULT);
    expect(NAV_PANEL_DEFAULT).toBe(225);
  });
});

describe('useSidebarLayout — recolher', () => {
  it('alterna e volta', () => {
    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    act(() => result.current.toggle());
    expect(result.current.collapsed).toBe(true);
    act(() => result.current.toggle());
    expect(result.current.collapsed).toBe(false);
  });

  it('recolher preserva a largura para quando expandir de novo', () => {
    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    act(() => result.current.commitWidth(300));
    act(() => result.current.toggle());
    act(() => result.current.toggle());
    expect(result.current.width).toBe(300);
  });
});

describe('useSidebarLayout — largura', () => {
  it('trava no mínimo e no máximo', () => {
    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    act(() => result.current.commitWidth(NAV_PANEL_MAX + 500));
    expect(result.current.width).toBe(NAV_PANEL_MAX);
    act(() => result.current.commitWidth(NAV_PANEL_MIN));
    expect(result.current.width).toBe(NAV_PANEL_MIN);
  });

  it('abaixo do mínimo recolhe, em vez de travar na borda', () => {
    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    act(() => result.current.commitWidth(NAV_PANEL_MIN - 1));
    expect(result.current.collapsed).toBe(true);
    // a largura anterior fica de pé: expandir devolve algo utilizável, não 179px
    expect(result.current.width).toBe(NAV_PANEL_DEFAULT);
  });

  it('definir uma largura válida expande uma sidebar recolhida', () => {
    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    act(() => result.current.toggle());
    act(() => result.current.commitWidth(260));
    expect(result.current.collapsed).toBe(false);
    expect(result.current.width).toBe(260);
  });
});

describe('useSidebarLayout — persistência', () => {
  it('o estado sobrevive à remontagem', () => {
    const first = renderHook(() => useSidebarLayout(), { wrapper });
    act(() => first.result.current.commitWidth(340));
    act(() => first.result.current.toggle());
    first.unmount();

    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    expect(result.current.collapsed).toBe(true);
    expect(result.current.width).toBe(340);
  });

  it('valor salvo fora da faixa cai no padrão, em vez de propagar lixo', () => {
    window.localStorage.setItem(WIDTH_KEY, '9999');
    expect(renderHook(() => useSidebarLayout(), { wrapper }).result.current.width).toBe(NAV_PANEL_DEFAULT);
    window.localStorage.setItem(WIDTH_KEY, 'nao-e-numero');
    expect(renderHook(() => useSidebarLayout(), { wrapper }).result.current.width).toBe(NAV_PANEL_DEFAULT);
  });

  it('localStorage indisponível não quebra a renderização', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });
    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    expect(result.current.width).toBe(NAV_PANEL_DEFAULT);
    expect(result.current.collapsed).toBe(false);
    expect(() => act(() => result.current.toggle())).not.toThrow();
    expect(result.current.collapsed).toBe(true);
  });

  it('grava as duas preferências', () => {
    const { result } = renderHook(() => useSidebarLayout(), { wrapper });
    act(() => result.current.commitWidth(280));
    act(() => result.current.toggle());
    expect(window.localStorage.getItem(WIDTH_KEY)).toBe('280');
    expect(window.localStorage.getItem(COLLAPSED_KEY)).toBe('true');
  });
});
