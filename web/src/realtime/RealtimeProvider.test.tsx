import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { auth } from '../api';
import { queryKeys } from '../queries';
import { RealtimeProvider } from './RealtimeProvider';
import { usePresence } from './usePresence';
import { openRealtimeStream } from './stream';
import type { RealtimeSignal } from './stream';

vi.mock('./stream', () => ({ openRealtimeStream: vi.fn(() => () => {}) }));

function renderProvider(qc: QueryClient) {
  return render(
    <QueryClientProvider client={qc}>
      <RealtimeProvider>
        <div>conteúdo</div>
      </RealtimeProvider>
    </QueryClientProvider>,
  );
}

/** Pega os callbacks que o provider passou pro stream mockado — é como o teste dispara
 *  onOpen/onSignal sem precisar simular fetch/SSE de verdade (isso já é coberto em stream.test.ts). */
function capturedOptions() {
  return vi.mocked(openRealtimeStream).mock.calls.at(-1)![0];
}

describe('RealtimeProvider', () => {
  beforeEach(() => {
    auth.set('jwt-de-teste');
    vi.mocked(openRealtimeStream).mockClear();
  });

  afterEach(() => {
    auth.clear();
  });

  it('onOpen invalida notificações, contagem não lida (via queryKeys) e presença', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    renderProvider(qc);
    await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));

    act(() => capturedOptions().onOpen());

    // invalidateAfterCancel cancela antes de invalidar (fix da engolida durante o 1º fetch) — a
    // chamada de invalidateQueries só acontece depois do `cancelQueries(...).then(...)` resolver,
    // um tick depois do onOpen síncrono. `waitFor` cobre esse microtask sem fixar o número exato.
    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['notifications'] });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.notificationsUnread });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['presence'] });
    });
  });

  it('sinal de notificação invalida notificações e contagem não lida, mas não presença', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    renderProvider(qc);
    await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));
    invalidateSpy.mockClear();

    const signal: RealtimeSignal = { kind: 'notification', resourceId: null };
    act(() => capturedOptions().onSignal(signal));

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['notifications'] });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.notificationsUnread });
    });
    expect(invalidateSpy).not.toHaveBeenCalledWith({ queryKey: ['presence'] });
  });

  it('sinal de presença com resourceId invalida só a chave daquele recurso', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    renderProvider(qc);
    await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));
    invalidateSpy.mockClear();

    // `resourceId` aqui precisa ser o formato REAL que o servidor manda no sinal — um Guid hifenizado
    // (`resourceId.ToString()`, forma D; ver EnterPresence.NotifyChanged), nunca um id prefixado tipo
    // `user_abc`. `usePresence` normaliza sua chave de query para esse mesmo formato hifenizado
    // (ver `usePresence.ts`); um valor de teste fictício deixa este teste "correto" com qualquer
    // convenção de id, mascarando um descompasso de verdade entre a chave da query e a chave que este
    // sinal invalida (revisão da Task 8, CRITICAL 2).
    const signal: RealtimeSignal = { kind: 'presence', resourceId: '3fa85f64-5717-4562-b3fc-2c963f66afa6' };
    act(() => capturedOptions().onSignal(signal));

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith({
        queryKey: queryKeys.presence('3fa85f64-5717-4562-b3fc-2c963f66afa6'),
      });
    });
    expect(invalidateSpy).not.toHaveBeenCalledWith({ queryKey: ['notifications'] });
  });

  it('sinal de presença chegando durante o primeiro fetch não fica engolido (regressão de produção)', async () => {
    // Porta a reprodução de presence-staleness-investigation.md §1 pro caminho de produção real —
    // RealtimeProvider.onSignal disparando o efeito, não uma chamada direta de invalidateQueries como
    // nos testes acima. Esses testes acima só provam que `invalidateQueries` foi chamado com a chave
    // certa, o que a forma ingênua (`qc.invalidateQueries`, sem cancelar antes) também satisfaz —
    // não pegam a regressão que motivou o fix. Este teste falha se os seis call sites de
    // RealtimeProvider voltarem a usar a forma ingênua.
    const RID_PREFIXED = 'req_a1d820ad19e44a5bb7daf3ba33b2d4aa';
    const RID_DASHED = 'a1d820ad-19e4-4a5b-b7da-f3ba33b2d4aa';
    const json = (body: unknown) =>
      new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });

    const getUrls: string[] = [];
    let resolveFirst!: (res: Response) => void;
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation((_url, init) => {
      if ((init?.method ?? 'GET') !== 'GET') return Promise.resolve(json([]));
      getUrls.push('GET');
      // GET #1 = snapshot de ANTES do outro usuário entrar; resolvido à mão, depois do sinal chegar.
      if (getUrls.length === 1) return new Promise<Response>((resolve) => (resolveFirst = resolve));
      return Promise.resolve(json([{ userId: 'u-2', name: 'Outro Usuário' }]));
    });

    try {
      const qc = new QueryClient({
        defaultOptions: { queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false } }, // = queryClient.ts
      });
      const wrapper = ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={qc}>
          <RealtimeProvider>{children}</RealtimeProvider>
        </QueryClientProvider>
      );

      const { result, unmount } = renderHook(() => usePresence('requisition', RID_PREFIXED), { wrapper });
      await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));
      await waitFor(() => expect(getUrls).toHaveLength(1));

      // O sinal SSE real chega ENQUANTO o GET #1 (o primeiro fetch da query) ainda está em voo.
      act(() => capturedOptions().onSignal({ kind: 'presence', resourceId: RID_DASHED }));

      await act(async () => {
        resolveFirst(json([]));
        await new Promise((resolve) => setTimeout(resolve, 0));
      });

      await waitFor(() => expect(result.current.users).toHaveLength(1));
      expect(getUrls).toHaveLength(2);

      act(() => unmount());
    } finally {
      fetchSpy.mockRestore();
    }
  });
});
