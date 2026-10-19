import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';
import type { AiClassificationSuggestion } from './api';
import { useAiClassificationSuggestion } from './queries';

// Mock the api module so that `aiClassificationSuggestion` is a controllable spy.
// We preserve everything else from the real module so the hook's import resolves.
vi.mock('./api', async (orig) => {
  const actual = await orig<typeof import('./api')>();
  return {
    ...actual,
    api: {
      ...(actual.api as object),
      aiClassificationSuggestion: vi.fn(),
    },
  };
});

// Import after mocking so we get the spied version.
import { api } from './api';

const mockResponse: AiClassificationSuggestion = {
  accountingAccountId: 'chart_account_acc123',
  confidence: 0.87,
  rationale: 'Vendor historically classifies here.',
  source: 'ai',
};

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client: qc }, children);
}

describe('useAiClassificationSuggestion', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls the ai-classification-suggestion endpoint via POST and resolves the response', async () => {
    const spy = vi.mocked(api.aiClassificationSuggestion).mockResolvedValueOnce(mockResponse);

    const { result } = renderHook(() => useAiClassificationSuggestion(), { wrapper: makeWrapper() });

    act(() => {
      result.current.mutate('payable_abc123');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Assert the spy was called with the correct payable id.
    expect(spy).toHaveBeenCalledOnce();
    expect(spy).toHaveBeenCalledWith('payable_abc123');

    // Assert the resolved data exposes all required fields.
    const data = result.current.data;
    expect(data).toBeDefined();
    expect(data?.source).toBe('ai');
    expect(data?.confidence).toBe(0.87);
    expect(data?.rationale).toBe('Vendor historically classifies here.');
    expect(data?.accountingAccountId).toBe('chart_account_acc123');
  });

  it('surfaces errors from the endpoint', async () => {
    vi.mocked(api.aiClassificationSuggestion).mockRejectedValueOnce(new Error('Not Found'));

    const { result } = renderHook(() => useAiClassificationSuggestion(), { wrapper: makeWrapper() });

    act(() => {
      result.current.mutate('payable_missing');
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as Error).message).toBe('Not Found');
  });

  it('exposes source="heuristic" when AI is unavailable', async () => {
    const heuristicResponse: AiClassificationSuggestion = {
      accountingAccountId: 'chart_account_heur',
      confidence: 0,
      rationale: 'Based on prior classifications for this vendor.',
      source: 'heuristic',
    };
    vi.mocked(api.aiClassificationSuggestion).mockResolvedValueOnce(heuristicResponse);

    const { result } = renderHook(() => useAiClassificationSuggestion(), { wrapper: makeWrapper() });

    act(() => {
      result.current.mutate('payable_xyz');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.source).toBe('heuristic');
    expect(result.current.data?.accountingAccountId).toBe('chart_account_heur');
  });
});
