import { describe, it, expect } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useAsync } from './useAsync';

describe('useAsync', () => {
  it('goes loading -> success and stores data', async () => {
    const { result } = renderHook(() => useAsync(async (x: number) => x * 2));

    expect(result.current.loading).toBe(false);

    let promise: Promise<unknown>;
    act(() => {
      promise = result.current.run(21);
    });
    await act(async () => {
      await promise;
    });

    expect(result.current.loading).toBe(false);
    expect(result.current.data).toBe(42);
    expect(result.current.error).toBeNull();
  });

  it('goes loading -> error and captures the message', async () => {
    const { result } = renderHook(() =>
      useAsync(async () => {
        throw new Error('boom');
      }),
    );

    await act(async () => {
      await result.current.run();
    });

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
      expect(result.current.error).toBe('boom');
      expect(result.current.data).toBeNull();
    });
  });
});
