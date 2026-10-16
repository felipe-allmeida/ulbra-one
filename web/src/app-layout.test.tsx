import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { auth } from './api';
import { openRealtimeStream } from './realtime/stream';
import { useRealtimeConnectionId } from './realtime/context';
import { AppLayout } from './app-layout';

// O chrome não interessa aqui — o que este teste observa é o CONTEÚDO DA ROTA, que é onde as telas com
// presença (PresenceAvatars) vivem.
vi.mock('./components/SideNav', () => ({ SideNav: () => null }));
vi.mock('./components/Topbar', () => ({ Topbar: () => null }));
vi.mock('./analytics', () => ({ useClaritySession: () => {} }));
vi.mock('./impersonation', () => ({ ImpersonationProvider: ({ children }: { children: ReactNode }) => children }));
vi.mock('./store', () => ({
  AppDataProvider: ({ children }: { children: ReactNode }) => children,
  useApp: () => ({ empresaId: 'empresa-1' }),
}));
vi.mock('./realtime/stream', () => ({ openRealtimeStream: vi.fn(() => () => {}) }));

function ConnectionIdProbe() {
  const connectionId = useRealtimeConnectionId();
  return <div data-testid='probe'>{connectionId ?? 'sem-conexão'}</div>;
}

/**
 * A costura que nenhum teste pegava: mover o `<RealtimeProvider>` para dentro/fora do lugar errado
 * (envolvendo o `ClaritySession` mas NÃO o `Shell`, por exemplo) passa no typecheck, deixa o sino
 * funcionando — o QueryClient é global, então a invalidação por sinal continua chegando — e mata a
 * presença em silêncio para sempre: `useRealtimeConnectionId` devolve null fora do provider e
 * `usePresence` sai cedo, sem erro, sem log, sem teste vermelho. Remover o provider inteiro também é
 * pego aqui, mas o deslocamento é a regressão realista, e é a que o typecheck não vê.
 */
describe('AppLayout — o RealtimeProvider envolve o conteúdo da rota', () => {
  beforeEach(() => {
    auth.set('jwt-de-teste'); // sem token o provider nem abre o stream
    vi.mocked(openRealtimeStream).mockClear();
  });

  afterEach(() => {
    auth.clear();
  });

  it('o conteúdo da rota enxerga o connectionId da conexão SSE', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/app/probe']}>
          <Routes>
            <Route path='/app' element={<AppLayout />}>
              <Route path='probe' element={<ConnectionIdProbe />} />
            </Route>
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));
    expect(screen.getByTestId('probe')).toHaveTextContent('sem-conexão');

    // O sinal "connected" real, que é como o cliente aprende o id da própria conexão.
    const options = vi.mocked(openRealtimeStream).mock.calls.at(-1)![0];
    act(() => options.onSignal({ kind: 'connected', resourceId: 'conn-42' }));

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('conn-42'));
  });
});
