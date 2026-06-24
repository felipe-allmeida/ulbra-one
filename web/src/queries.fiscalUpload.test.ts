import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';
import type { UploadTarget } from './api';
import { useRequestFiscalUploadUrl, uploadToPresignedUrl } from './queries';

// Mock the api module — replace only the fiscal upload-url method.
vi.mock('./api', async (orig) => {
  const actual = await orig<typeof import('./api')>();
  return {
    ...actual,
    api: {
      ...(actual.api as object),
      requestFiscalUploadUrl: vi.fn(),
    },
  };
});

import { api } from './api';

const mockTarget: UploadTarget = {
  uploadUrl: 'https://blob.example.com/uploads/nf-test.xml?sig=abc',
  key: 'fiscal/2024/nf-test.xml',
};

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client: qc }, children);
}

describe('useRequestFiscalUploadUrl', () => {
  beforeEach(() => vi.clearAllMocks());

  it('POSTs to /payables/fiscal-document/upload-url and returns uploadUrl + key', async () => {
    const spy = vi.mocked(api.requestFiscalUploadUrl).mockResolvedValueOnce(mockTarget);

    const { result } = renderHook(() => useRequestFiscalUploadUrl(), { wrapper: makeWrapper() });

    act(() => {
      result.current.mutate({ fileName: 'nf-test.xml' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(spy).toHaveBeenCalledOnce();
    expect(spy).toHaveBeenCalledWith('nf-test.xml');

    expect(result.current.data?.uploadUrl).toBe(mockTarget.uploadUrl);
    expect(result.current.data?.key).toBe(mockTarget.key);
  });

  it('surfaces errors from the endpoint', async () => {
    vi.mocked(api.requestFiscalUploadUrl).mockRejectedValueOnce(new Error('Storage.PresignNotSupported'));

    const { result } = renderHook(() => useRequestFiscalUploadUrl(), { wrapper: makeWrapper() });

    act(() => {
      result.current.mutate({ fileName: 'nf.xml' });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as Error).message).toContain('PresignNotSupported');
  });
});

describe('uploadToPresignedUrl', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('PUTs the file to the presigned URL with the correct headers', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(null, { status: 200 }));

    const file = new File(['<nfeProc/>'], 'nf.xml', { type: 'application/xml' });
    await uploadToPresignedUrl('https://sas.example.com/upload?sig=xyz', file);

    expect(fetchSpy).toHaveBeenCalledOnce();
    const [url, init] = fetchSpy.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('https://sas.example.com/upload?sig=xyz');
    expect(init.method).toBe('PUT');
    expect(init.body).toBe(file);
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/xml');
    expect((init.headers as Record<string, string>)['x-ms-blob-type']).toBe('BlockBlob');
  });

  it('throws when the PUT returns a non-OK status', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(null, { status: 403, statusText: 'Forbidden' }));

    const file = new File(['data'], 'nf.xml', { type: 'text/xml' });
    await expect(uploadToPresignedUrl('https://sas.example.com/upload', file)).rejects.toThrow('403');
  });
});
