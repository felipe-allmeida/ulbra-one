import { describe, it, expect, vi, afterEach } from 'vitest';
import { openRealtimeStream } from './stream';
import { auth } from '../api';

function streamResponse(chunks: string[], status = 200): Response {
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      const encoder = new TextEncoder();
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
      controller.close();
    },
  });
  return new Response(body, { status, headers: { 'Content-Type': 'text/event-stream' } });
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

describe('openRealtimeStream', () => {
  it('entrega cada sinal e ignora heartbeat', async () => {
    const signals: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          streamResponse([': ping\n\n', 'data: {"kind":"notification","resourceId":null}\n\n', ': ping\n\n']),
        ),
    );

    const close = openRealtimeStream({
      token: 'jwt',
      onSignal: (s) => signals.push(s),
      onOpen: () => {},
      reconnect: false,
    });
    await vi.waitFor(() => expect(signals).toHaveLength(1));
    close();

    expect(signals[0]).toEqual({ kind: 'notification', resourceId: null });
  });

  it('manda o token no header Authorization', async () => {
    const fetchMock = vi.fn().mockResolvedValue(streamResponse([]));
    vi.stubGlobal('fetch', fetchMock);

    const close = openRealtimeStream({ token: 'jwt', onSignal: () => {}, onOpen: () => {}, reconnect: false });
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    close();

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(new Headers(init.headers).get('Authorization')).toBe('Bearer jwt');
  });

  it('chama onOpen a cada conexão, não só na primeira', async () => {
    // reconnect: false (como nos outros testes) não consegue provar isto — com uma única conexão
    // possível, "dispara em toda conexão" e "dispara uma vez só" são indistinguíveis. É a mesma
    // invalidação de onOpen que fecha o buraco de um sinal perdido numa queda sem redelivery, então
    // o teste só vale alguma coisa se reconectar de verdade e contar pelo menos 2 chamadas.
    vi.useFakeTimers();
    const onOpen = vi.fn();
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(() => Promise.resolve(streamResponse([': ping\n\n']))),
    );

    const close = openRealtimeStream({ token: 'jwt', onSignal: () => {}, onOpen, reconnect: true });

    await vi.advanceTimersByTimeAsync(0); // primeira conexão
    await vi.advanceTimersByTimeAsync(1_000); // backoff inicial até a segunda conexão
    close();

    expect(onOpen.mock.calls.length).toBeGreaterThanOrEqual(2);
  });

  it('não deixa sinal partido entre chunks se perder', async () => {
    const signals: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(streamResponse(['data: {"kind":"noti', 'fication","resourceId":"abc"}\n\n'])),
    );

    const close = openRealtimeStream({
      token: 'jwt',
      onSignal: (s) => signals.push(s),
      onOpen: () => {},
      reconnect: false,
    });
    await vi.waitFor(() => expect(signals).toHaveLength(1));
    close();

    expect(signals[0]).toEqual({ kind: 'notification', resourceId: 'abc' });
  });

  it('entrega sinal terminado em CRLF, não só LF', async () => {
    const signals: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(streamResponse(['data: {"kind":"notification","resourceId":"abc"}\r\n\r\n'])),
    );

    const close = openRealtimeStream({
      token: 'jwt',
      onSignal: (s) => signals.push(s),
      onOpen: () => {},
      reconnect: false,
    });
    await vi.waitFor(() => expect(signals).toHaveLength(1));
    close();

    expect(signals[0]).toEqual({ kind: 'notification', resourceId: 'abc' });
  });

  it('cresce o backoff exponencialmente quando o corpo termina sem dado (EOF imediato)', async () => {
    // Uma réplica drenando (deploy/scale-in) encerra o corpo normalmente e na hora, sem erro —
    // é exatamente o `streamResponse([])` abaixo. Resetar o backoff no fetch resolvido (em vez de
    // no primeiro byte de verdade) faz esse caso reconectar a 1/s pra sempre; o teste prova que o
    // intervalo entre onOpen dobra, e não fica achatado em FIRST_RETRY_MS.
    vi.useFakeTimers();
    const opens: number[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(() => Promise.resolve(streamResponse([]))),
    );

    const close = openRealtimeStream({
      token: 'jwt',
      onSignal: () => {},
      onOpen: () => opens.push(Date.now()),
      reconnect: true,
    });

    await vi.advanceTimersByTimeAsync(0);
    await vi.advanceTimersByTimeAsync(1_000);
    await vi.advanceTimersByTimeAsync(2_000);
    await vi.advanceTimersByTimeAsync(4_000);
    await vi.advanceTimersByTimeAsync(8_000);
    close();

    expect(opens).toHaveLength(5);
    const deltas = opens.slice(1).map((t, i) => t - opens[i]);
    expect(deltas).toEqual([1_000, 2_000, 4_000, 8_000]);
  });

  it('para de tentar em 401 e desloga como o resto da app (api.ts)', async () => {
    const originalLocation = window.location;
    const assign = vi.fn();
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...originalLocation, pathname: '/app', assign },
    });
    const clearSpy = vi.spyOn(auth, 'clear');

    const fetchMock = vi.fn().mockResolvedValue(streamResponse([], 401));
    vi.stubGlobal('fetch', fetchMock);

    const close = openRealtimeStream({ token: 'jwt-expirado', onSignal: () => {}, onOpen: () => {}, reconnect: true });
    await vi.waitFor(() => expect(assign).toHaveBeenCalledWith('/login'));
    close();

    // Dá tempo pra um retry indevido aparecer, se a guarda não tivesse cortado o loop.
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(clearSpy).toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
  });

  it('para de tentar em 403 sem deslogar — não é sessão expirada, é permissão', async () => {
    const clearSpy = vi.spyOn(auth, 'clear');
    const fetchMock = vi.fn().mockResolvedValue(streamResponse([], 403));
    vi.stubGlobal('fetch', fetchMock);

    const close = openRealtimeStream({ token: 'jwt', onSignal: () => {}, onOpen: () => {}, reconnect: true });
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    close();

    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(clearSpy).not.toHaveBeenCalled();
  });

  it('não chama onOpen nem onSignal se close() correr antes do fetch resolver', async () => {
    const onOpen = vi.fn();
    const onSignal = vi.fn();
    let resolveFetch!: (res: Response) => void;
    const pending = new Promise<Response>((resolve) => {
      resolveFetch = resolve;
    });
    // O mock ignora o AbortSignal de propósito — é o cenário onde só a guarda em `stopped`
    // (e não o abort da rede) impede o callback de disparar depois do close().
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(pending));

    const close = openRealtimeStream({ token: 'jwt', onSignal, onOpen, reconnect: false });
    close();
    resolveFetch(streamResponse(['data: {"kind":"notification","resourceId":null}\n\n']));

    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(onOpen).not.toHaveBeenCalled();
    expect(onSignal).not.toHaveBeenCalled();
  });
});
