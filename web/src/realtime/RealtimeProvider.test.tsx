import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { auth } from '../api';
import { queryKeys } from '../queries';
import { RealtimeProvider } from './RealtimeProvider';
import { openRealtimeStream } from './stream';
import type { RealtimeSignal } from './stream';

vi.mock('./stream', () => ({ openRealtimeStream: vi.fn(() => () => {}) }));

function renderProvider(qc: QueryClient) {
  return render(
    <QueryClientProvider client={qc}>
      <RealtimeProvider>
        <div>conteúdo</div>
      </RealtimeProvider>
    </QueryClientProvider>,
  );
}

/** Pega os callbacks que o provider passou pro stream mockado — é como o teste dispara
 *  onOpen/onSignal sem precisar simular fetch/SSE de verdade (isso já é coberto em stream.test.ts). */
function capturedOptions() {
  return vi.mocked(openRealtimeStream).mock.calls.at(-1)![0];
}

describe('RealtimeProvider', () => {
  beforeEach(() => {
    auth.set('jwt-de-teste');
    vi.mocked(openRealtimeStream).mockClear();
  });

  afterEach(() => {
    auth.clear();
  });

  it('onOpen invalida notificações, contagem não lida (via queryKeys) e presença', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    renderProvider(qc);
    await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));

    act(() => capturedOptions().onOpen());

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['notifications'] });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.notificationsUnread });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['presence'] });
    });
  });

  it('sinal de notificação invalida notificações e contagem não lida, mas não presença', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    renderProvider(qc);
    await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));
    invalidateSpy.mockClear();

    const signal: RealtimeSignal = { kind: 'notification', resourceId: null };
    act(() => capturedOptions().onSignal(signal));

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['notifications'] });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.notificationsUnread });
    });
    expect(invalidateSpy).not.toHaveBeenCalledWith({ queryKey: ['presence'] });
  });

  it('sinal de presença com resourceId invalida só a chave daquele recurso', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    renderProvider(qc);
    await waitFor(() => expect(openRealtimeStream).toHaveBeenCalledTimes(1));
    invalidateSpy.mockClear();

    // `resourceId` aqui precisa ser o formato REAL que o servidor manda no sinal — um Guid hifenizado
    // (`resourceId.ToString()`, forma D; ver EnterPresence.NotifyChanged), nunca um id prefixado tipo
    // `user_abc`. `usePresence` normaliza sua chave de query para esse mesmo formato hifenizado
    // (ver `usePresence.ts`); um valor de teste fictício deixa este teste "correto" com qualquer
    // convenção de id, mascarando um descompasso de verdade entre a chave da query e a chave que este
    // sinal invalida (revisão da Task 8, CRITICAL 2).
    const signal: RealtimeSignal = { kind: 'presence', resourceId: '3fa85f64-5717-4562-b3fc-2c963f66afa6' };
    act(() => capturedOptions().onSignal(signal));

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith({
        queryKey: queryKeys.presence('3fa85f64-5717-4562-b3fc-2c963f66afa6'),
      });
    });
    expect(invalidateSpy).not.toHaveBeenCalledWith({ queryKey: ['notifications'] });
  });


});
