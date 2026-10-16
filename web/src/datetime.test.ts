import { describe, it, expect } from 'vitest';
import { dayBucket, dayEndIso, dayStartIso, relativeTime } from './datetime';

describe('dayStartIso / dayEndIso', () => {
  it('turns a date input into the instant that opens that day locally', () => {
    const d = new Date(dayStartIso('2026-08-20')!);
    expect([d.getFullYear(), d.getMonth() + 1, d.getDate()]).toEqual([2026, 8, 20]);
    expect([d.getHours(), d.getMinutes(), d.getSeconds()]).toEqual([0, 0, 0]);
  });

  it('turns a date input into the instant that closes that day locally', () => {
    const d = new Date(dayEndIso('2026-08-20')!);
    expect(d.getDate()).toBe(20);
    expect([d.getHours(), d.getMinutes(), d.getSeconds(), d.getMilliseconds()]).toEqual([23, 59, 59, 999]);
  });

  it('reads a blank input as "no bound", so the filter is left off the request', () => {
    expect(dayStartIso('')).toBeUndefined();
    expect(dayEndIso('')).toBeUndefined();
  });
});

describe('relativeTime', () => {
  const now = new Date(2026, 7, 27, 15, 30, 0); // 27/08/2026 15:30 local
  const at = (y: number, mo: number, d: number, h = 12, mi = 0) => new Date(y, mo - 1, d, h, mi).toISOString();

  it('collapses the last minute into "agora"', () => {
    expect(relativeTime(at(2026, 8, 27, 15, 30), now)).toEqual({ key: 'now' });
  });

  it('counts minutes inside the hour and hours inside the day', () => {
    expect(relativeTime(at(2026, 8, 27, 15, 18), now)).toEqual({ key: 'minutes', n: 12 });
    expect(relativeTime(at(2026, 8, 27, 9, 30), now)).toEqual({ key: 'hours', n: 6 });
  });

  // Calendar-day based, not 24h based: 23:00 yesterday is "ontem" even though it is 16h ago.
  it('names the previous calendar day instead of counting hours', () => {
    expect(relativeTime(at(2026, 8, 26, 23, 0), now)).toEqual({ key: 'yesterday' });
  });

  it('counts days up to a week, then weeks', () => {
    expect(relativeTime(at(2026, 8, 25, 14, 11), now)).toEqual({ key: 'days', n: 2 });
    expect(relativeTime(at(2026, 8, 21, 8, 0), now)).toEqual({ key: 'days', n: 6 });
    expect(relativeTime(at(2026, 8, 20, 8, 0), now)).toEqual({ key: 'weeks', n: 1 });
    expect(relativeTime(at(2026, 8, 6, 8, 0), now)).toEqual({ key: 'weeks', n: 3 });
  });

  it('falls back to the absolute date once "há N sem" stops meaning anything', () => {
    expect(relativeTime(at(2026, 6, 30, 8, 0), now)).toEqual({ key: 'absolute' });
  });

  it('never reports the future as elapsed time (clock skew between server and browser)', () => {
    expect(relativeTime(at(2026, 8, 27, 15, 32), now)).toEqual({ key: 'now' });
  });
});

describe('dayBucket', () => {
  const now = new Date(2026, 7, 27, 15, 30, 0);
  const at = (d: number, h = 12) => new Date(2026, 7, d, h).toISOString();

  it('buckets by calendar distance, not by elapsed hours', () => {
    expect(dayBucket(at(27, 0), now)).toBe('today');
    expect(dayBucket(at(26, 23), now)).toBe('yesterday');
    expect(dayBucket(at(21), now)).toBe('week');
    expect(dayBucket(at(20), now)).toBe('older');
  });
});
