import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { auth } from './api';
import { queryClient } from './queryClient';

const TOKEN_KEY = 'ulbra-one.token';

/** base64url-encode an ASCII JSON object (JWT segment). */
function b64url(obj: unknown): string {
  return btoa(JSON.stringify(obj)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** base64url-encode via UTF-8 bytes, mirroring how the backend serializes claims. */
function b64urlUtf8(obj: unknown): string {
  const bytes = new TextEncoder().encode(JSON.stringify(obj));
  let bin = '';
  bytes.forEach((b) => (bin += String.fromCharCode(b)));
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Build a fake 3-segment JWT with the given payload (signature is irrelevant client-side). */
function makeToken(payload: Record<string, unknown>, encode = b64url): string {
  return `${b64url({ alg: 'HS256', typ: 'JWT' })}.${encode(payload)}.sig`;
}

const nowSec = () => Math.floor(Date.now() / 1000);

describe('auth.hasValidToken', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => {
    localStorage.clear();
    vi.useRealTimers();
  });

  it('is false when no token is stored', () => {
    expect(auth.hasValidToken()).toBe(false);
  });

  it('is false for a malformed (non-JWT) token', () => {
    localStorage.setItem(TOKEN_KEY, 'garbage-not-a-jwt');
    expect(auth.hasValidToken()).toBe(false);
  });

  it('is false when the token has fewer than 3 segments', () => {
    localStorage.setItem(TOKEN_KEY, `${b64url({ alg: 'HS256' })}.${b64url({ exp: nowSec() + 3600 })}`);
    expect(auth.hasValidToken()).toBe(false);
  });

  it('is true for a token whose exp is well in the future', () => {
    localStorage.setItem(TOKEN_KEY, makeToken({ exp: nowSec() + 3600 }));
    expect(auth.hasValidToken()).toBe(true);
  });

  it('is false for a token expired beyond the 1-min skew grace', () => {
    localStorage.setItem(TOKEN_KEY, makeToken({ exp: nowSec() - 120 }));
    expect(auth.hasValidToken()).toBe(false);
  });

  it('is true within the 1-min clock-skew grace (matches backend ClockSkew)', () => {
    localStorage.setItem(TOKEN_KEY, makeToken({ exp: nowSec() - 30 }));
    expect(auth.hasValidToken()).toBe(true);
  });

  it('is false when the payload has no exp claim', () => {
    localStorage.setItem(TOKEN_KEY, makeToken({ sub: 'user-1' }));
    expect(auth.hasValidToken()).toBe(false);
  });

  it('is false when exp is not numeric', () => {
    localStorage.setItem(TOKEN_KEY, makeToken({ exp: 'soon' }));
    expect(auth.hasValidToken()).toBe(false);
  });

  it('reads exp even when the payload carries multibyte UTF-8 claims (e.g. accented name)', () => {
    localStorage.setItem(TOKEN_KEY, makeToken({ name: 'José Contábil', exp: nowSec() + 3600 }, b64urlUtf8));
    expect(auth.hasValidToken()).toBe(true);
  });
});

// Session/role change: a token change must reset the React Query cache, otherwise the post-login/logout
// SPA navigation keeps the previous user's `useMe` (permissions) and the UI stays on the wrong profile.
describe('auth session reset — token change clears the React Query cache', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('auth.clear() clears the cache (logout, even on a role switch)', () => {
    const spy = vi.spyOn(queryClient, 'clear');
    auth.clear();
    expect(spy).toHaveBeenCalledTimes(1);
  });

  it('auth.set() clears the cache and stores the token (new role fetches /auth/me fresh)', () => {
    const spy = vi.spyOn(queryClient, 'clear');
    auth.set(makeToken({ exp: nowSec() + 3600 }));
    expect(spy).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem(TOKEN_KEY)).not.toBeNull();
  });
});
