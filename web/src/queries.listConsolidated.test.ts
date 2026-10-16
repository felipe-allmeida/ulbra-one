import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';

// Mock the api module so listPayables / listReceivables are controllable spies while the rest resolves.
vi.mock('./api', async (orig) => {
  const actual = await orig<typeof import('./api')>();
  return {
    ...actual,
    api: {
      ...(actual.api as object),
      listPayables: vi.fn(),
      listReceivables: vi.fn(),
      listMyRequisitionDecisions: vi.fn(),
      listMyRequisitionTriageDecisions: vi.fn(),
    },
  };
});

import { api } from './api';
import {
  ALL_COMPANIES,
  useMyRequisitionDecisions,
  useMyRequisitionTriageDecisions,
  usePayables,
  useReceivables,
} from './queries';

const emptyPage = { items: [], page: 1, pageSize: 20, total: 0, totalPages: 1 };

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client: qc }, children);
}

describe('usePayables / useReceivables — consolidated "Todas" (AEL-906)', () => {
  beforeEach(() => vi.clearAllMocks());

  it('usePayables omits companyId when the sentinel ALL_COMPANIES is selected', async () => {
    const spy = vi.mocked(api.listPayables).mockResolvedValue(emptyPage);
    renderHook(() => usePayables({ companyId: ALL_COMPANIES }), { wrapper: makeWrapper() });
    await waitFor(() => expect(spy).toHaveBeenCalled());
    // Sending '__all__' literal would 400 (not a Guid) — the hook must strip it to undefined.
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ companyId: undefined }));
  });

  it('usePayables passes a real companyId through unchanged', async () => {
    const spy = vi.mocked(api.listPayables).mockResolvedValue(emptyPage);
    renderHook(() => usePayables({ companyId: 'company_1', status: 'Scheduled' }), { wrapper: makeWrapper() });
    await waitFor(() => expect(spy).toHaveBeenCalled());
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ companyId: 'company_1', status: 'Scheduled' }));
  });

  it('useReceivables omits companyId when the sentinel ALL_COMPANIES is selected', async () => {
    const spy = vi.mocked(api.listReceivables).mockResolvedValue(emptyPage);
    renderHook(() => useReceivables({ companyId: ALL_COMPANIES }), { wrapper: makeWrapper() });
    await waitFor(() => expect(spy).toHaveBeenCalled());
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ companyId: undefined }));
  });

  it('useReceivables passes a real companyId through unchanged', async () => {
    const spy = vi.mocked(api.listReceivables).mockResolvedValue(emptyPage);
    renderHook(() => useReceivables({ companyId: 'company_1' }), { wrapper: makeWrapper() });
    await waitFor(() => expect(spy).toHaveBeenCalled());
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ companyId: 'company_1' }));
  });

  // The inbox "Histórico" tabs (my-decisions) — scoped to the caller, so the sentinel just drops the filter.
  it('useMyRequisitionDecisions omits companyId for the sentinel', async () => {
    const spy = vi.mocked(api.listMyRequisitionDecisions).mockResolvedValue(emptyPage);
    renderHook(() => useMyRequisitionDecisions(ALL_COMPANIES, 1), { wrapper: makeWrapper() });
    await waitFor(() => expect(spy).toHaveBeenCalled());
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ companyId: undefined, page: 1 }));
  });

  it('useMyRequisitionTriageDecisions omits companyId for the sentinel', async () => {
    const spy = vi.mocked(api.listMyRequisitionTriageDecisions).mockResolvedValue(emptyPage);
    renderHook(() => useMyRequisitionTriageDecisions(ALL_COMPANIES, 1), { wrapper: makeWrapper() });
    await waitFor(() => expect(spy).toHaveBeenCalled());
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ companyId: undefined, page: 1 }));
  });
});
