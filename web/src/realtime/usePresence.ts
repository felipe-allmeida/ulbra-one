import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type PresenceUser } from '../api';
import { ApiError } from '../apiError';
import { queryKeys } from '../queries';
import { useRealtimeConnectionId } from './RealtimeProvider';

/**
 * Contador de `seq` por conexão SSE — estritamente crescente, nunca reinicia enquanto a conexão durar.
 * É o que desempata a ordem entre um enter e um leave quando os dois publishes (roteados por Redis)
 * caem em réplicas diferentes: um `seq` repetido ou menor faz o backend descartar o comando — e um
 * leave descartado deixa presença fantasma até o próximo heartbeat renovar o TTL (o enter já aplicou).
 * Compartilhado entre recursos de propósito: só precisa ser crescente, não precisa reiniciar por
 * recurso, porque o backend guarda o último `seq` aplicado por `(connectionId, scope, resourceId)` —
 * cada tripla vê apenas uma subsequência dele, que continua crescente.
 * Módulo (não `useRef`): o contador tem que sobreviver a remontagens do hook para o mesmo recurso
 * (ex.: reabrir a mesma tela na mesma aba) — reiniciar em 1 reenviaria um `seq` já aplicado
 * (ver `usePresence.test.tsx`, "seq é estritamente crescente entre remontagens").
 * Uma nova conexão (reconexão) chega com um `connectionId` novo, então a chave nunca colide com a
 * conexão anterior — não é preciso limpar o mapa.
 */
const seqByConnection = new Map<string, number>();

function nextSeq(connectionId: string): number {
  const seq = (seqByConnection.get(connectionId) ?? 0) + 1;
  seqByConnection.set(connectionId, seq);
  return seq;
}

/**
 * Tentativas de sair da presença. O `seq` é alocado UMA vez e reusado em todas — nunca renovado por
 * tentativa: uma retentativa que chega depois de um enter novo (o usuário reabriu o mesmo recurso)
 * precisa ser DESCARTADA pelo backend por ter seq menor. Com um seq novo ela passaria à frente do enter
 * e apagaria uma presença legítima.
 *
 * Por que existe: um leave perdido NÃO é coberto pelo TTL. O TTL de 45 s só resolve quando a CONEXÃO SSE
 * morre (aba fechada, réplica derrubada) — enquanto ela vive, a entrada local no
 * RealtimeConnectionRegistry sobrevive e o `RenewPresenceAsync` replica-level (deliberadamente sem
 * `When.Exists`) RECRIA o membro no Redis a cada heartbeat. O fantasma dura o que durar o stream: horas,
 * não 45 s. O caso realista é navegar dentro da SPA (a aba continua viva, o stream continua aberto)
 * enquanto o DELETE cai numa réplica drenando ou num blip de rede.
 * Enquanto a aba vive dá para retentar; quando ela morre, o stream morre junto e aí sim o TTL resolve.
 */
const LEAVE_RETRY_DELAYS_MS = [1_000, 4_000];

async function leaveWithRetry(connectionId: string, scope: string, resourceId: string, seq: number): Promise<void> {
  for (let attempt = 0; ; attempt++) {
    try {
      await api.leavePresence(connectionId, scope, resourceId, seq);
      return;
    } catch (e) {
      // 4xx é definitivo (validação, sessão expirada): retentar só repete o mesmo erro. Erro de rede e
      // 5xx — inclusive o de uma réplica drenando — são os que valem a próxima tentativa.
      const definitive = e instanceof ApiError && e.status < 500;
      if (definitive || attempt >= LEAVE_RETRY_DELAYS_MS.length) return;
      await new Promise((resolve) => setTimeout(resolve, LEAVE_RETRY_DELAYS_MS[attempt]));
    }
  }
}

/**
 * Normaliza `resourceId` para o Guid hifenizado (`D`, ex. `3fa85f64-5717-4562-b3fc-2c963f66afa6`) —
 * a ÚNICA representação usada tanto na chamada de rede quanto na chave de query deste hook.
 * Duas razões pra isto ser feito uma vez só, aqui:
 *  1. O corpo JSON do `POST /realtime/presence` é `System.Text.Json`, que só aceita Guid no formato
 *     `D`; o formato `N` (32 hex sem hífen, que é como um id tipado como `req_{guid:N}` chega) falha
 *     a desserialização — 400 em produção, 500 em Development.
 *  2. O sinal SSE de presença que o servidor manda carrega `resourceId.ToString()` — Guid `D` — e
 *     `RealtimeProvider` invalida `queryKeys.presence(signal.resourceId)` com esse valor tal como
 *     chega. Se a chave de query deste hook usasse outra representação (o id prefixado, por exemplo),
 *     a invalidação por sinal nunca bateria com a chave, e reabrir a mesma requisição em outra aba
 *     nunca atualizaria esta.
 * Ids tipados do domínio chegam prefixados (`req_...`); a conexão SSE (`connectionId`) já chega crua.
 */
function normalizeResourceId(id: string): string {
  const bare = id
    .replace(/^[a-z]+_/, '')
    .replace(/-/g, '')
    .toLowerCase();
  return `${bare.slice(0, 8)}-${bare.slice(8, 12)}-${bare.slice(12, 16)}-${bare.slice(16, 20)}-${bare.slice(20)}`;
}

/** Quem está vendo este recurso agora, com nome já resolvido (`GetPresence` injeta `IUserDirectory` —
 *  o endpoint já autoriza contra o recurso, então devolver o nome de quem está presente não é uma
 *  divulgação nova). Sem timer: a renovação do TTL é do servidor — um timer por réplica no
 *  `RealtimeBackplaneListener` renova a presença de todas as conexões locais de uma vez. Um timer de
 *  renovação aqui duplicaria isso e é exatamente o que este hook não deve reintroduzir (ver
 *  `usePresence.test.tsx`). */
export function usePresence(scope: string, resourceId: string): { users: PresenceUser[] } {
  const connectionId = useRealtimeConnectionId();
  const qc = useQueryClient();
  const guidResourceId = normalizeResourceId(resourceId);

  const { data } = useQuery({
    queryKey: queryKeys.presence(guidResourceId),
    queryFn: () => api.getPresence(scope, guidResourceId),
    staleTime: Infinity, // atualiza só por sinal SSE de presença, nunca por tempo
  });

  useEffect(() => {
    if (!connectionId) return;
    // Best-effort: uma rejeição aqui (rede caída, 401 de sessão expirada) não deve virar uma promise
    // não tratada — o canal de presença é acessório, o TTL de 45s do backend já cobre a falha.
    void api.enterPresence(connectionId, scope, guidResourceId, nextSeq(connectionId)).catch(() => {});

    return () => {
      // Best-effort com retentativa limitada, e nunca uma promise rejeitada solta: o canal de presença é
      // acessório (ver leaveWithRetry, que também explica por que o TTL não cobre um leave perdido).
      void leaveWithRetry(connectionId, scope, guidResourceId, nextSeq(connectionId));
      void qc.invalidateQueries({ queryKey: queryKeys.presence(guidResourceId) });
    };
  }, [connectionId, scope, guidResourceId, qc]);

  return { users: data ?? [] };
}
