import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';
import { auth } from '../api';
import { queryKeys } from '../queries';
import { usePresence } from './usePresence';
import { RealtimeProvider } from './RealtimeProvider';
import { openRealtimeStream } from './stream';

// `usePresence` só entra em presença com um `connectionId` real (vindo do sinal SSE "connected" —
// ver RealtimeProvider). Mockar `./stream` — como `RealtimeProvider.test.tsx` já faz — deixa o teste
// disparar esse sinal na hora certa, sem simular fetch/SSE de verdade.
vi.mock('./stream', () => ({ openRealtimeStream: vi.fn(() => () => {}) }));

function capturedOptions() {
  return vi.mocked(openRealtimeStream).mock.calls.at(-1)![0];
}

/** `fetch`'s first arg is `RequestInfo | URL`, never a plain string statically — but `request()` (api.ts)
 *  always calls it with a template-string URL, so this just narrows that back to a string safely. */
function urlOf(input: RequestInfo | URL): string {
  return typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
}

/** Espera um macrotask real — microtasks SEMPRE terminam de esvaziar antes do próximo macrotask
 *  rodar, então isto garante que uma cadeia de promise pendente (fetch → checagem de status → texto →
 *  parse → .catch) tenha assentado de vez, mesmo sob fake timers (`advanceTimersByTimeAsync(0)` só
 *  garante UM tick). Sem isto, uma chamada de saída (leave) que ainda não assentou quando `afterEach`
 *  restaura o `fetch` de verdade acaba usando o `fetch` REAL — contra um servidor que pode nem existir,
 *  ou que devolve 401 e dispara `window.location.assign`, que o jsdom não implementa e que corrompe o
 *  teste seguinte (o 401 zera `auth.token`).  */
function flushMacrotask() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

// `renderWithProviders` (web/src/test/renderWithProviders.tsx) monta `render(ui, options)`, uma
// assinatura incompatível com o `wrapper` de `renderHook` (que precisa de um componente
// `({children}) => ReactElement`) — por isso um wrapper próprio aqui, no mesmo formato que
// `queries.listConsolidated.test.ts` já usa para hooks que precisam de QueryClientProvider.
function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, createElement(RealtimeProvider, null, children));
}

beforeEach(() => {
  auth.set('jwt-de-teste'); // RealtimeProvider só conecta com um token — sem isto o connectionId nunca chega
  vi.mocked(openRealtimeStream).mockClear();
});

afterEach(() => {
  auth.clear();
  vi.restoreAllMocks();
});

describe('usePresence', () => {
  it('entra ao montar e sai ao desmontar', async () => {
    const calls: string[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation((_url, init) => {
      calls.push(init?.method ?? 'GET');
      return Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }));
    });

    const { unmount } = renderHook(() => usePresence('requisition', 'r-1'), { wrapper: makeWrapper() });
    await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));
    act(() => capturedOptions().onSignal({ kind: 'connected', resourceId: 'conn-1' }));

    await waitFor(() => expect(calls).toContain('POST'));
    act(() => unmount());
    await waitFor(() => expect(calls).toContain('DELETE'));
    await flushMacrotask();
  });

  it('não dispara timer periódico — a renovação é do servidor', async () => {
    vi.useFakeTimers();
    // Uma Response só pode ter o corpo lido uma vez — `mockResolvedValue` reusaria a mesma instância
    // pras três chamadas do hook (GET + POST no mount, DELETE no unmount) e a segunda leitura
    // quebraria com "Body is unusable". `mockImplementation` cria uma nova a cada chamada.
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(() =>
        Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })),
      );

    const { unmount } = renderHook(() => usePresence('requisition', 'r-1'), { wrapper: makeWrapper() });
    await act(async () => {
      capturedOptions().onSignal({ kind: 'connected', resourceId: 'conn-1' });
      await vi.advanceTimersByTimeAsync(0); // deixa o efeito de entrada e a query de leitura resolverem
    });

    const afterMount = fetchMock.mock.calls.length;
    expect(afterMount).toBeGreaterThan(0);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(120_000);
    });

    expect(fetchMock.mock.calls.length).toBe(afterMount);
    await act(async () => {
      unmount();
      await vi.advanceTimersByTimeAsync(0); // deixa a saída (leave) do unmount disparar antes do mock ser restaurado
    });
    vi.useRealTimers();
    // Volta a timers de verdade ANTES de esvaziar — sob fake timers um único `advanceTimersByTimeAsync(0)`
    // só garante um tick de microtask, não a cadeia inteira do `leavePresence` (fetch → status → texto →
    // parse → .catch); um macrotask real drena tudo o que ficou pendente.
    await flushMacrotask();
  });

  // Regressão da revisão da Task 8 (CRITICAL 2): `usePresence` registrava sua query com o id
  // PREFIXADO (`req_...`), mas o sinal SSE que o servidor manda carrega `resourceId.ToString()` — Guid
  // hifenizado. `RealtimeProvider` invalidava `queryKeys.presence('<dashed>')` enquanto a query vivia
  // em `queryKeys.presence('req_...')` — as chaves nunca batiam, e presença nunca atualizava por sinal
  // (só no reconnect, que invalida o prefixo inteiro `['presence']`). Este teste finge exatamente esse
  // sinal — a chave que ele invalida precisa ser a MESMA que este hook usa, ou o refetch abaixo nunca
  // acontece e o `waitFor` estoura.
  it('usa a chave de query no formato hifenizado — o mesmo que o sinal de presença do servidor carrega', async () => {
    const getUrls: string[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation((url, init) => {
      if ((init?.method ?? 'GET') === 'GET') getUrls.push(urlOf(url));
      return Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }));
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: qc }, createElement(RealtimeProvider, null, children));

    // Id tipado prefixado, exatamente como `requisition.id` chega na tela de detalhe.
    const { unmount } = renderHook(() => usePresence('requisition', 'req_a1d820ad19e44a5bb7daf3ba33b2d4aa'), {
      wrapper,
    });
    act(() => capturedOptions().onSignal({ kind: 'connected', resourceId: 'conn-1' }));
    await waitFor(() => expect(getUrls.length).toBe(1));

    // O sinal real que o servidor manda para este recurso (`RealtimeSignal("presence", resourceId.ToString())`):
    // Guid forma D, hifenizado — não o id prefixado.
    await act(async () => {
      await qc.invalidateQueries({ queryKey: queryKeys.presence('a1d820ad-19e4-4a5b-b7da-f3ba33b2d4aa') });
    });

    // Só bate se a chave da query for a MESMA (hifenizada) — senão o invalidate é um no-op e getUrls fica em 1.
    await waitFor(() => expect(getUrls.length).toBe(2));

    // Desmontar aqui — DENTRO do teste, não via cleanup implícito do testing-library no afterEach — é
    // o que garante que a saída (leave) disparada por isto assente (flushMacrotask) enquanto o fetch
    // mockado deste teste ainda está de pé. Um unmount implícito rodaria DEPOIS que este `it` já
    // retornou, correndo contra `vi.restoreAllMocks()` do `afterEach` — e um leave que perde essa
    // corrida cai no `fetch` de verdade, contra um servidor que talvez nem exista, e se devolver 401
    // dispara `window.location.assign` (não implementado no jsdom) bem no meio do próximo teste.
    act(() => unmount());
    await flushMacrotask();
  });
});
