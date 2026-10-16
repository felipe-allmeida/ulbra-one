import { describe, it, expect, beforeEach, afterEach, vi, type MockInstance } from 'vitest';
import { api } from './api';

const page = { items: [], page: 1, pageSize: 20, total: 0, totalPages: 0 };
const okResponse = () =>
  new Response(JSON.stringify(page), { status: 200, headers: { 'Content-Type': 'application/json' } });

/** The path + query string the client actually requested. */
function requestedUrl(spy: MockInstance<typeof fetch>): URL {
  const [url] = spy.mock.calls[0];
  // The client always calls fetch with a string path; anything else means the call under test changed shape.
  if (typeof url !== 'string') throw new Error('expected fetch to be called with a URL string');
  return new URL(url, 'http://localhost');
}

describe('api.listAuditLogs', () => {
  beforeEach(() => vi.restoreAllMocks());
  afterEach(() => vi.restoreAllMocks());

  it('sends every provided filter as a query parameter', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(okResponse());

    await api.listAuditLogs({
      entityType: 'Payable',
      entityId: 'pay_1',
      actorUserId: 'user_7',
      from: '2026-08-01T00:00:00.000Z',
      to: '2026-08-31T23:59:59.999Z',
      page: 2,
      pageSize: 25,
    });

    const url = requestedUrl(spy);
    expect(url.pathname).toMatch(/\/audit-logs$/);
    expect(Object.fromEntries(url.searchParams)).toEqual({
      entityType: 'Payable',
      entityId: 'pay_1',
      actorUserId: 'user_7',
      from: '2026-08-01T00:00:00.000Z',
      to: '2026-08-31T23:59:59.999Z',
      page: '2',
      pageSize: '25',
    });
  });

  it('omits filters that were left blank', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(okResponse());

    await api.listAuditLogs({ entityType: '', entityId: undefined, page: 1 });

    expect([...requestedUrl(spy).searchParams.keys()]).toEqual(['page']);
  });
});
