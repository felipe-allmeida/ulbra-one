import { createContext, useContext } from 'react';

export type RealtimeStatus = 'connecting' | 'open' | 'closed';

/**
 * Contextos e hooks de leitura do realtime moram aqui, e não em `RealtimeProvider.tsx`, por um motivo
 * só: `queries.ts` precisa de `useRealtimeStatus` (o fallback do sino, ver `useUnreadNotificationsCount`)
 * e `RealtimeProvider.tsx` precisa de `queryKeys` — um importando o outro fecharia um ciclo de módulos.
 * Este arquivo não importa nada da app, então quem precisar do status importa daqui, nunca do provider.
 */
export const RealtimeStatusContext = createContext<RealtimeStatus>('closed');

/** Id da conexão SSE atual — chega no sinal "connected" e é o único jeito do cliente aprender seu
 *  próprio id, para as chamadas de presença. `null` antes da primeira conexão e durante reconexão. */
export const RealtimeConnectionIdContext = createContext<string | null>(null);

export function useRealtimeStatus(): RealtimeStatus {
  return useContext(RealtimeStatusContext);
}

export function useRealtimeConnectionId(): string | null {
  return useContext(RealtimeConnectionIdContext);
}
