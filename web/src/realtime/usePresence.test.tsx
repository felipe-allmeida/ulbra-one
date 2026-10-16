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

  // Um DELETE perdido NÃO é coberto pelo TTL enquanto a conexão SSE vive: a entrada local no registry
  // sobrevive e a renovação replica-level (sem `When.Exists`) recria o membro no Redis a cada heartbeat.
  // O fantasma durava o que durasse o stream — horas. O caso realista é navegar dentro da SPA (aba viva,
  // stream aberto) e o DELETE cair numa réplica drenando (5xx) ou num blip de rede.
  it('retenta o leave quando a requisição falha, com o MESMO seq', async () => {
    vi.useFakeTimers();
    const leaves: number[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation((url, init) => {
      const method = init?.method ?? 'GET';
      if (method === 'DELETE') {
        leaves.push(Number(new URL(urlOf(url), 'http://localhost').searchParams.get('seq')));
        if (leaves.length === 1) return Promise.reject(new TypeError('Failed to fetch'));
      }
      return Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }));
    });

    const { unmount } = renderHook(() => usePresence('requisition', 'r-retry'), { wrapper: makeWrapper() });
    await act(async () => {
      capturedOptions().onSignal({ kind: 'connected', resourceId: 'conn-retry' });
      await vi.advanceTimersByTimeAsync(0);
    });

    await act(async () => {
      unmount();
      await vi.advanceTimersByTimeAsync(2_000); // o primeiro backoff é de 1 s
    });

    expect(leaves).toHaveLength(2);
    // Mesmo seq nas duas: uma retentativa com seq novo passaria à frente de um enter posterior (o
    // usuário reabrindo o mesmo recurso) e apagaria uma presença legítima.
    expect(leaves[1]).toBe(leaves[0]);
    vi.useRealTimers();
    await flushMacrotask();
  });

  // Regressão da revisão da Task 8 (IMPORTANT 3): nada garantia que o `seq` fosse estritamente
  // crescente entre remontagens do hook na MESMA conexão (ex.: reabrir a mesma requisição na mesma
  // aba). Um `useRef` local ao hook — a alternativa óbvia — reinicia em 1 a cada montagem nova; este
  // teste falha contra essa implementação porque o segundo "entrar" repetiria o seq=1 já usado no
  // primeiro ciclo, e o backend descartaria esse enter/leave em silêncio (ver o comentário do módulo
  // `seqByConnection` em `usePresence.ts`).
  it('seq é estritamente crescente entre remontagens na mesma conexão', async () => {
    const seqs: number[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation((url, init) => {
      const method = init?.method ?? 'GET';
      if (method === 'POST') {
        const seq = (JSON.parse(init?.body as string) as { seq: number }).seq;
        seqs.push(seq);
      } else if (method === 'DELETE') {
        const seq = new URL(urlOf(url), 'http://localhost').searchParams.get('seq');
        if (seq !== null) seqs.push(Number(seq));
      }
      return Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }));
    });

    const wrapper = makeWrapper();
    const CONNECTION_ID = 'conn-fixo-para-remontagem';

    const { unmount: unmount1 } = renderHook(() => usePresence('requisition', 'r-seq'), { wrapper });
    act(() => capturedOptions().onSignal({ kind: 'connected', resourceId: CONNECTION_ID }));
    await waitFor(() => expect(seqs.length).toBeGreaterThanOrEqual(1)); // enter #1

    act(() => unmount1());
    await waitFor(() => expect(seqs.length).toBeGreaterThanOrEqual(2)); // leave #1

    const { unmount: unmount2 } = renderHook(() => usePresence('requisition', 'r-seq'), { wrapper });
    act(() => capturedOptions().onSignal({ kind: 'connected', resourceId: CONNECTION_ID }));
    await waitFor(() => expect(seqs.length).toBeGreaterThanOrEqual(3)); // enter #2 (mesma conexão)

    act(() => unmount2());
    await waitFor(() => expect(seqs.length).toBeGreaterThanOrEqual(4)); // leave #2

    for (let i = 1; i < seqs.length; i++) expect(seqs[i]).toBeGreaterThan(seqs[i - 1]);
    await flushMacrotask();
  });
});
