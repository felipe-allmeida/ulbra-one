import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createQueryClient } from './queryClient';
import { useUnreadNotificationsCount } from './queries';

/**
 * O §7.4 do design promete que, sem realtime, "o cliente volta ao refetch on-focus e o sino fica
 * atrasado, como hoje. Regressão para o comportamento atual, não indisponibilidade". Estes testes são
 * o que impede essa promessa de sumir de novo: a branch que removeu o `refetchInterval` de 30 s deixou
 * o sino SEM nenhuma fonte de atualização por tempo — e o sino mora no `Topbar`, fora do `KeyedOutlet`,
 * então nem `refetchOnMount` volta a disparar depois da primeira montagem. Sem sinal SSE (Redis fora,
 * proxy bufferizando, publish engolido) o badge congelava pelo resto da sessão.
 *
 * Os dois casos usam `createQueryClient()` — o cliente REAL da app, com `refetchOnWindowFocus: false`
 * global — de propósito: um `QueryClient` de teste com defaults próprios provaria só que o React Query
 * funciona, não que a configuração que roda em produção atualiza o sino.
 */
function makeWrapper() {
  const qc = createQueryClient();
  return ({ children }: { children: ReactNode }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('useUnreadNotificationsCount — fallback quando o push falha', () => {
  let counts: number[];

  beforeEach(() => {
    counts = [1, 2, 3];
    vi.spyOn(globalThis, 'fetch').mockImplementation(() =>
      Promise.resolve(
        new Response(JSON.stringify({ count: counts.shift() ?? 99 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    );
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('atualiza por tempo mesmo sem nenhum sinal — o piso do §7.4', async () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useUnreadNotificationsCount(), { wrapper: makeWrapper() });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(result.current.data?.count).toBe(1);

    // Nenhum sinal SSE, nenhuma mutação, nenhuma navegação — só o tempo passando. Sem o piso, o badge
    // ficaria em 1 para sempre.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(61_000);
    });

    // Sem `waitFor` aqui: sob fake timers ele competiria com o relógio que este teste controla —
    // `advanceTimersByTimeAsync` já drenou o refetch inteiro (fetch mockado, resolvido na hora).
    expect(result.current.data?.count).toBe(2);
  });

  it('atualiza ao focar a janela, apesar do refetchOnWindowFocus: false global', async () => {
    const { result } = renderHook(() => useUnreadNotificationsCount(), { wrapper: makeWrapper() });
    await waitFor(() => expect(result.current.data?.count).toBe(1));

    // A query nasce com `staleTime: 30_000` (queryClient.ts); o refetch on-focus só dispara com o dado
    // stale, então o teste avança o relógio do React Query em vez de esperar 30 s de verdade.
    const realNow = Date.now;
    vi.spyOn(Date, 'now').mockImplementation(() => realNow() + 60_000);
    try {
      act(() => {
        window.dispatchEvent(new Event('visibilitychange'));
        window.dispatchEvent(new Event('focus'));
      });
      await waitFor(() => expect(result.current.data?.count).toBe(2));
    } finally {
      vi.mocked(Date.now).mockRestore();
    }
  });
});
