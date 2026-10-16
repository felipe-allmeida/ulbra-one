import { useCallback, useState } from 'react';
import { getErrorMessage } from './apiError';

interface AsyncState<T> {
  loading: boolean;
  error: string | null;
  data: T | null;
  /** Executes `fn`; resolves to the value (or null on error). Errors are captured in `error`. */
  run: (...args: unknown[]) => Promise<T | null>;
}

/** Wraps an async fn with loading/error/data state. Replaces silent `.catch(() => {})`. */
export function useAsync<T>(fn: (...args: any[]) => Promise<T>): AsyncState<T> {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<T | null>(null);

  const run = useCallback(
    async (...args: unknown[]): Promise<T | null> => {
      setLoading(true);
      setError(null);
      try {
        const result = await fn(...args);
        setData(result);
        return result;
      } catch (e) {
        setError(getErrorMessage(e));
        return null;
      } finally {
        setLoading(false);
      }
    },
    [fn],
  );

  return { loading, error, data, run };
}
