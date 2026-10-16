import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { auth } from '../api';
import { queryKeys } from '../queries';
import { openRealtimeStream, type RealtimeSignal } from './stream';

type Status = 'connecting' | 'open' | 'closed';

const RealtimeContext = createContext<Status>('closed');

export function useRealtimeStatus(): Status {
  return useContext(RealtimeContext);
}

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<Status>('closed');
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
        void qc.invalidateQueries({ queryKey: ['notifications'] });
        void qc.invalidateQueries({ queryKey: queryKeys.notificationsUnread });
        void qc.invalidateQueries({ queryKey: ['presence'] });
      },
      onSignal: (signal: RealtimeSignal) => {
        if (signal.kind === 'notification') {
          void qc.invalidateQueries({ queryKey: ['notifications'] });
          void qc.invalidateQueries({ queryKey: queryKeys.notificationsUnread });
        }
        if (signal.kind === 'presence' && signal.resourceId) {
          void qc.invalidateQueries({ queryKey: ['presence', signal.resourceId] });
        }
      },
    });

    return () => {
      close();
      setStatus('closed');
    };
  }, [token, qc]);

  return <RealtimeContext.Provider value={status}>{children}</RealtimeContext.Provider>;
}
