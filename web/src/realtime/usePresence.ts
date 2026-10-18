import { useEffect, useRef } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type PresenceUser } from '../api';
import { queryKeys } from '../queries';
import { invalidateAfterCancel } from './invalidateAfterCancel';
import { useRealtimeConnectionId } from './RealtimeProvider';

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
  // Contador de `seq` desta tela: crescente entre o enter e o leave, que é o que desempata a ordem quando
  // os dois publishes caem em réplicas diferentes (o backend descarta um comando com seq menor ou igual ao
  // último aplicado).
  const seq = useRef(0);

  const { data } = useQuery({
    queryKey: queryKeys.presence(guidResourceId),
    queryFn: () => api.getPresence(scope, guidResourceId),
    staleTime: Infinity, // atualiza só por sinal SSE de presença, nunca por tempo
  });

  useEffect(() => {
    if (!connectionId) return;
    // Best-effort: uma rejeição aqui (rede caída, 401 de sessão expirada) não deve virar uma promise
    // não tratada — o canal de presença é acessório, o TTL de 45s do backend já cobre a falha.
    void api.enterPresence(connectionId, scope, guidResourceId, ++seq.current).catch(() => {});

    return () => {
      // Best-effort, e nunca uma promise rejeitada solta: o canal de presença é acessório — se o DELETE não
      // chegar, o TTL de 45 s do backend resolve.
      void api.leavePresence(connectionId, scope, guidResourceId, ++seq.current).catch(() => {});
      // Mesmo helper dos call sites do RealtimeProvider — não porque este caso seja vulnerável hoje
      // (o observer já foi desinscrito quando este cleanup roda, então não há fetch em voo pra
      // engolir a invalidação), mas pra não deixar a forma ingênua como exemplo a copiar quando
      // alguém mexer aqui depois.
      invalidateAfterCancel(qc, queryKeys.presence(guidResourceId));
    };
  }, [connectionId, scope, guidResourceId, qc]);

  return { users: data ?? [] };
}
