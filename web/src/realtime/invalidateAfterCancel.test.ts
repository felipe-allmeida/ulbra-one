import { describe, it, expect, vi, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { StrictMode, createElement, type ReactNode } from 'react';
import { invalidateAfterCancel } from './invalidateAfterCancel';

/** Mesmos defaults de `createQueryClient` (`../queryClient.ts`) — a query do sino usa exatamente
 *  isto: `staleTime` alto e sem refetch por foco é o que torna o engolimento definitivo (nada
 *  dispara de novo sozinho depois que a resposta em voo do primeiro fetch chega). */
function makeClient() {
  return new QueryClient({
    defaultOptions: { queries: { staleTime: 30_000, retry: false, refetchOnWindowFocus: false } },
  });
}

function wrapperFor(qc: QueryClient, strict = false) {
  return ({ children }: { children: ReactNode }) => {
    const provider = createElement(QueryClientProvider, { client: qc }, children);
    return strict ? createElement(StrictMode, null, provider) : provider;
  };
}

afterEach(() => vi.useRealTimers());

describe('invalidateAfterCancel', () => {
  it('sinal chegando durante o primeiro fetch não é engolido pelo dedupe do query-core', async () => {
    // Reproduz Query.fetch (query-core@5.101.0): com `state.data === undefined` (primeiro fetch
    // da vida da query), ele devolve a promise já em voo em vez de cancelar-e-refazer. Uma
    // invalidação simples (`qc.invalidateQueries`, sem cancelar antes) morre aqui: a resposta em
    // voo carrega o snapshot de ANTES do sinal, e a action de sucesso zera `isInvalidated` — sem
    // nada de timer/foco pra tentar de novo, o dado errado fica pra sempre.
    const qc = makeClient();
    let resolveFirst!: (value: number) => void;
    const queryFn = vi
      .fn()
      .mockImplementationOnce(() => new Promise<number>((resolve) => (resolveFirst = resolve)))
      .mockResolvedValue(2); // qualquer fetch além do primeiro já reflete a mudança

    const { result } = renderHook(() => useQuery({ queryKey: ['probe'], queryFn }), { wrapper: wrapperFor(qc) });
    await waitFor(() => expect(queryFn).toHaveBeenCalledTimes(1));

    // O sinal chega enquanto o primeiro fetch ainda está em voo — state.data ainda é undefined.
    invalidateAfterCancel(qc, ['probe']);

    resolveFirst(1); // resposta do fetch original chega DEPOIS do sinal, com o valor pré-mudança

    await waitFor(() => expect(result.current.data).toBe(2));
    expect(queryFn).toHaveBeenCalledTimes(2); // 1 original (engolido) + 1 pra pegar a mudança
  });

  it('rajada de 3 sinais durante o primeiro fetch produz dado correto com só 2 GETs (sem amplificar)', async () => {
    const qc = makeClient();
    let resolveFirst!: (value: number) => void;
    const queryFn = vi
      .fn()
      .mockImplementationOnce(() => new Promise<number>((resolve) => (resolveFirst = resolve)))
      .mockResolvedValue(4); // reflete a mudança mais recente da rajada

    const { result } = renderHook(() => useQuery({ queryKey: ['probe-burst'], queryFn }), {
      wrapper: wrapperFor(qc),
    });
    await waitFor(() => expect(queryFn).toHaveBeenCalledTimes(1));

    // 3 sinais em rajada, todos ainda com o primeiro fetch em voo — não pode virar 1 GET por sinal.
    invalidateAfterCancel(qc, ['probe-burst']);
    invalidateAfterCancel(qc, ['probe-burst']);
    invalidateAfterCancel(qc, ['probe-burst']);

    resolveFirst(1);

    await waitFor(() => expect(result.current.data).toBe(4));
    expect(queryFn).toHaveBeenCalledTimes(2);
  });

  it('continua correto em StrictMode (efeitos de assinatura montam duas vezes)', async () => {
    const qc = makeClient();
    let resolveFirst!: (value: number) => void;
    const queryFn = vi
      .fn()
      .mockImplementationOnce(() => new Promise<number>((resolve) => (resolveFirst = resolve)))
      .mockResolvedValue(2);

    const { result } = renderHook(() => useQuery({ queryKey: ['probe-strict'], queryFn }), {
      wrapper: wrapperFor(qc, true),
    });
    await waitFor(() => expect(queryFn).toHaveBeenCalled());

    invalidateAfterCancel(qc, ['probe-strict']);
    resolveFirst(1);

    await waitFor(() => expect(result.current.data).toBe(2));
  });

  it('não introduz request periódico — 120s de timers falsos sem nenhum GET extra', async () => {
    // Sanity check pro "sem polling" que a Task 6 removeu: nada no helper (nem no efeito que o
    // chama) pode ter reintroduzido um `setInterval`/`refetchInterval` disfarçado.
    vi.useFakeTimers();
    const qc = makeClient();
    const queryFn = vi.fn().mockResolvedValue(1);

    renderHook(() => useQuery({ queryKey: ['probe-idle'], queryFn }), { wrapper: wrapperFor(qc) });
    await vi.waitFor(() => expect(queryFn).toHaveBeenCalledTimes(1));

    await act(() => vi.advanceTimersByTimeAsync(120_000));

    expect(queryFn).toHaveBeenCalledTimes(1);
  });
});
