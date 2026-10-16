import { getApiBaseUrl } from '../config/env';
import { auth } from '../api';

export interface RealtimeSignal {
  kind: 'notification' | 'presence' | 'connected';
  resourceId: string | null;
}

interface Options {
  token: string;
  onSignal: (signal: RealtimeSignal) => void;
  /** Chamado a cada conexão estabelecida — o consumidor invalida tudo aqui, o que fecha o buraco
   *  dos sinais perdidos entre a queda e a reconexão sem replay no servidor. */
  onOpen: () => void;
  /** Chamado quando o stream desiste DE VEZ (401/403): não há reconexão, e o consumidor precisa saber
   *  disso para parar de se anunciar como conectado — um provider preso em 'connecting' para sempre faz
   *  o resto da app tratar como transitório algo que nunca mais volta. */
  onClosed?: () => void;
  /** Desligado nos testes, para o stream não reabrir após o corpo terminar. */
  reconnect?: boolean;
}

const FIRST_RETRY_MS = 1_000;
const MAX_RETRY_MS = 30_000;
/** Abre o canal SSE. Retorna a função que o fecha. */
export function openRealtimeStream({ token, onSignal, onOpen, onClosed, reconnect = true }: Options): () => void {
  const controller = new AbortController();
  let stopped = false;
  let retry = FIRST_RETRY_MS;

  async function connect(): Promise<void> {
    while (!stopped) {
      try {
        const res = await fetch(`${getApiBaseUrl()}/realtime/stream`, {
          headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' },
          signal: controller.signal,
        });
        if (stopped) return; // close() pode ter corrido enquanto o fetch estava em voo

        if (res.status === 401) {
          // Sessão expirada/inválida: não é uma queda passageira, retentar nunca resolve isto.
          // Mesmo tratamento que `api.ts` já dá pra todo outro 401 — limpa o token e manda pro
          // login — em vez de martelar 401 a cada ciclo de backoff pra sempre.
          auth.clear();
          if (!window.location.pathname.startsWith('/login')) {
            window.location.assign('/login');
          }
          onClosed?.();
          return;
        }
        if (res.status === 403) {
          // Sem permissão: não é transiente, retry não ajuda — mas o consumidor precisa saber que acabou.
          onClosed?.();
          return;
        }
        if (!res.ok || !res.body) throw new Error(`stream failed: ${res.status}`);

        onOpen();
        await read(res.body);
      } catch {
        // Queda de rede, deploy, scale-in: cai no backoff abaixo. Nada a reportar ao usuário —
        // sem stream a UI volta ao refetch on-focus, que é o comportamento anterior a esta feature.
      }

      if (stopped || !reconnect) return;
      await new Promise((resolve) => setTimeout(resolve, retry));
      retry = Math.min(retry * 2, MAX_RETRY_MS);
    }
  }

  async function read(body: ReadableStream<Uint8Array>): Promise<void> {
    const reader = body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let provedAlive = false;

    for (;;) {
      const { done, value } = await reader.read();
      if (done) return;

      // Reseta o backoff no primeiro dado recebido, não no fetch resolvido: um 200 que fecha sem
      // entregar nada não prova que a conexão está de pé.
      if (!provedAlive) {
        provedAlive = true;
        retry = FIRST_RETRY_MS;
      }

      buffer += decoder.decode(value, { stream: true });
      // SSE permite tanto LF quanto CRLF como fim de linha; normalizar aqui evita acoplar o
      // parser à implementação atual do servidor (que só emite LF) e a busca por "\n\n" abaixo
      // continua funcionando pros dois casos.
      buffer = buffer.replace(/\r\n/g, '\n');

      // Um evento SSE termina em linha em branco. Guardar o resto no buffer é o que impede um
      // sinal partido entre dois chunks de ser descartado.
      let boundary = buffer.indexOf('\n\n');
      while (boundary !== -1) {
        const event = buffer.slice(0, boundary);
        buffer = buffer.slice(boundary + 2);
        emit(event);
        boundary = buffer.indexOf('\n\n');
      }
    }
  }

  function emit(event: string): void {
    if (stopped) return; // close() pode ter corrido enquanto este chunk já estava em trânsito
    for (const line of event.split('\n')) {
      if (!line.startsWith('data: ')) continue; // ": ping" e afins
      try {
        onSignal(JSON.parse(line.slice(6)) as RealtimeSignal);
      } catch {
        // Linha malformada: ignorar é preferível a derrubar o canal por um sinal só.
      }
    }
  }

  void connect();

  return () => {
    stopped = true;
    controller.abort();
  };
}
