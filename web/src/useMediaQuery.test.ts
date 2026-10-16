import { describe, it, expect } from 'vitest';
import { useState } from 'react';
import { renderHook, act } from '@testing-library/react';
import { useMediaQuery } from './useMediaQuery';
import { setViewportMatches, type MockMediaQueryList } from './test/setup';

describe('useMediaQuery', () => {
  it('devolve false quando a media query não casa', () => {
    setViewportMatches(false);
    const { result } = renderHook(() => useMediaQuery('(max-width: 640px)'));
    expect(result.current).toBe(false);
  });

  it('devolve true quando a media query casa', () => {
    setViewportMatches(true);
    const { result } = renderHook(() => useMediaQuery('(max-width: 640px)'));
    expect(result.current).toBe(true);
  });

  it('subscreve e desinscreve de eventos change', () => {
    setViewportMatches(false);
    const { unmount } = renderHook(() => useMediaQuery('(max-width: 640px)'));

    const mql = window.matchMedia('(max-width: 640px)') as unknown as MockMediaQueryList;

    // addEventListener deve ter sido chamado com 'change'
    // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
    const addEventListenerMock = mql.addEventListener as any;
    expect(addEventListenerMock).toHaveBeenCalledWith('change', expect.any(Function));

    // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-explicit-any
    const changeHandler = (addEventListenerMock.mock.calls as any[]).find((call: any[]) => call[0] === 'change')?.[1];
    expect(changeHandler).toBeDefined();

    unmount();

    // removeEventListener deve ter sido chamado com 'change' e o mesmo handler
    // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
    const removeEventListenerMock = mql.removeEventListener as any;
    expect(removeEventListenerMock).toHaveBeenCalledWith('change', changeHandler);
  });

  it('atualiza quando setViewportMatches dispara evento change', () => {
    setViewportMatches(false);
    const { result } = renderHook(() => useMediaQuery('(max-width: 640px)'));
    expect(result.current).toBe(false);

    act(() => {
      setViewportMatches(true);
    });
    expect(result.current).toBe(true);

    act(() => {
      setViewportMatches(false);
    });
    expect(result.current).toBe(false);
  });

  it('não re-inscreve o listener de matchMedia quando o componente re-renderiza', () => {
    setViewportMatches(false);
    // Um hook auxiliar que força re-renders (via um contador de estado local) sem desmontar,
    // simulando o consumidor real: `usePhone()` chamado num componente que re-renderiza por
    // qualquer outro motivo. Antes do `useCallback` em `subscribe`, cada render passava uma nova
    // arrow para `useSyncExternalStore`, que desinscrevia e reinscrevia o listener de novo.
    const { result, rerender } = renderHook(() => {
      const [, setTick] = useState(0);
      const matches = useMediaQuery('(max-width: 640px)');
      return { matches, forceRerender: () => setTick((t) => t + 1) };
    });

    const mql = window.matchMedia('(max-width: 640px)') as unknown as MockMediaQueryList;
    expect(mql.addEventListener).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.forceRerender();
    });
    rerender();

    // Identidade estável de `subscribe` = nenhuma nova assinatura ao re-renderizar.
    expect(mql.addEventListener).toHaveBeenCalledTimes(1);
  });
});
