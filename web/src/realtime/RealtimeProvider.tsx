import { useEffect, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { auth } from '../api';
import { queryKeys } from '../queries';
import { RealtimeConnectionIdContext, RealtimeStatusContext, type RealtimeStatus as Status } from './context';
import { invalidateAfterCancel } from './invalidateAfterCancel';
import { openRealtimeStream, type RealtimeSignal } from './stream';

// Os hooks de leitura (useRealtimeStatus / useRealtimeConnectionId) vivem em ./context — ver o
// comentário lá: `queries.ts` consome o status e este arquivo consome `queryKeys`.
export { useRealtimeConnectionId, useRealtimeStatus } from './context';

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<Status>('closed');
  const [connectionId, setConnectionId] = useState<string | null>(null);
  const token = auth.token;

  useEffect(() => {
    if (!token) {
      setStatus('closed');
      return;
    }
    setStatus('connecting');

    const close = openRealtimeStream({
      token,
      onOpen: () => {
        setStatus('open');
        // Toda (re)conexão invalida o que o stream cobre: é isto que recupera os sinais perdidos
        // entre a queda e a reconexão, sem o servidor precisar guardar e reproduzir eventos.
        invalidateAfterCancel(qc, ['notifications']);
        invalidateAfterCancel(qc, queryKeys.notificationsUnread);
        invalidateAfterCancel(qc, ['presence']);
      },
      onClosed: () => setStatus('closed'),
      onSignal: (signal: RealtimeSignal) => {
        if (signal.kind === 'connected') setConnectionId(signal.resourceId);
        if (signal.kind === 'notification') {
          invalidateAfterCancel(qc, ['notifications']);
          invalidateAfterCancel(qc, queryKeys.notificationsUnread);
        }
        if (signal.kind === 'presence' && signal.resourceId) {
          invalidateAfterCancel(qc, queryKeys.presence(signal.resourceId));
        }
      },
    });

    return () => {
      close();
      setStatus('closed');
      // Reconexão gera um connectionId novo — um id velho sobrevivendo aqui faria a presença
      // ser registrada numa conexão que não existe mais.
      setConnectionId(null);
    };
  }, [token, qc]);

  return (
    <RealtimeStatusContext.Provider value={status}>
      <RealtimeConnectionIdContext.Provider value={connectionId}>{children}</RealtimeConnectionIdContext.Provider>
    </RealtimeStatusContext.Provider>
  );
}
