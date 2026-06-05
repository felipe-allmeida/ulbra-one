import { QueryClient } from '@tanstack/react-query';

/** Shared QueryClient with conservative defaults for this ERP's server state. */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: 1,
        refetchOnWindowFocus: false,
      },
    },
  });
}

export const queryClient = createQueryClient();
