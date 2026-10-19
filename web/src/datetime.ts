export const fmtDate = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');

/** Formats an ISO date-time (e.g. approvedAt/rejectedAt) as a pt-BR date. */
/** Data + hora. Era `toLocaleDateString`, que descartava a hora apesar do nome — a linha do tempo de um
 *  título mostrava quatro eventos "25/08/2026" sem dizer em que ordem do dia aconteceram. */
export const fmtDateTime = (iso: string) =>
  new Date(iso).toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });

/** Local date → 'YYYY-MM-DD' (DateOnly wire format). Never toISOString() — UTC-3 would shift the day. */
export const toDateOnly = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** First day of the current month as 'YYYY-MM-DD' (default `from` for the accounting statements). */
export const startOfCurrentMonth = () => {
  const now = new Date();
  return toDateOnly(new Date(now.getFullYear(), now.getMonth(), 1));
};

/** Last day of the current month as 'YYYY-MM-DD' (default `to`/`asOf` for the accounting statements).
 *  Day 0 of the next month is the last day of the current one. */
export const endOfCurrentMonth = () => {
  const now = new Date();
  return toDateOnly(new Date(now.getFullYear(), now.getMonth() + 1, 0));
};

const pad2 = (n: number) => String(n).padStart(2, '0');

/** ISO instant → 'dd/MM/yyyy HH:mm' in the reader's timezone. */
export const fmtInstant = (iso: string): string => {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return `${pad2(d.getDate())}/${pad2(d.getMonth() + 1)}/${d.getFullYear()} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`;
};

/** ISO instant → 'dd/MM/yyyy' (no time). What the inbox shows once "há N sem" stops meaning anything. */
export const fmtInstantDate = (iso: string): string => {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return `${pad2(d.getDate())}/${pad2(d.getMonth() + 1)}/${d.getFullYear()}`;
};

/** Parses a native date input ('YYYY-MM-DD'); null when it is blank or malformed. */
const dateParts = (date: string): [number, number, number] | null => {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
  return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
};

/**
 * Date input → the instant that day opens locally, as ISO. `undefined` for a blank input so the caller can
 * drop the bound instead of sending an empty filter. Built from the parts, never `new Date('YYYY-MM-DD')` —
 * that is UTC midnight and would include (or miss) hours of the neighbouring day.
 */
export const dayStartIso = (date: string): string | undefined => {
  const parts = dateParts(date);
  return parts ? new Date(parts[0], parts[1] - 1, parts[2]).toISOString() : undefined;
};

/** Date input → the last instant of that day locally, as ISO (inclusive upper bound). */
export const dayEndIso = (date: string): string | undefined => {
  const parts = dateParts(date);
  return parts ? new Date(parts[0], parts[1] - 1, parts[2], 23, 59, 59, 999).toISOString() : undefined;
};

/** Local date N days back, as 'YYYY-MM-DD' — the default lower bound of the audit filter. */
export const daysAgoDate = (days: number): string => {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return toDateOnly(d);
};

/** How long ago an instant was, as a translation key + the number to interpolate — the text itself stays
 *  in the i18n bundle (`notifications.relative.*`). Calendar-based, not elapsed-hours based: 23:00 of the
 *  previous day is "ontem", not "há 16 h", which is how a person reads their own inbox. Past four weeks
 *  the relative form stops meaning anything, so the caller falls back to the absolute date. */
export type RelativeTime =
  | { key: 'now' }
  | { key: 'yesterday' }
  | { key: 'absolute' }
  | { key: 'minutes' | 'hours' | 'days' | 'weeks'; n: number };

/** Midnight of the day an instant falls on, locally — the unit calendar comparisons count in. */
const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());

const calendarDaysBetween = (from: Date, to: Date) =>
  Math.round((startOfDay(to).getTime() - startOfDay(from).getTime()) / 86_400_000);

export const relativeTime = (iso: string, now: Date = new Date()): RelativeTime => {
  const then = new Date(iso);
  if (Number.isNaN(then.getTime())) return { key: 'absolute' };

  // A server clock a few seconds ahead of the browser would otherwise render as "há -1 min".
  const elapsedMinutes = Math.floor((now.getTime() - then.getTime()) / 60_000);
  if (elapsedMinutes < 1) return { key: 'now' };

  const days = calendarDaysBetween(then, now);
  if (days === 0)
    return elapsedMinutes < 60
      ? { key: 'minutes', n: elapsedMinutes }
      : { key: 'hours', n: Math.floor(elapsedMinutes / 60) };
  if (days === 1) return { key: 'yesterday' };
  if (days < 7) return { key: 'days', n: days };
  if (days < 28) return { key: 'weeks', n: Math.floor(days / 7) };
  return { key: 'absolute' };
};

/** Which day-group header a notification sits under in the inbox. */
export type DayBucket = 'today' | 'yesterday' | 'week' | 'older';

export const dayBucket = (iso: string, now: Date = new Date()): DayBucket => {
  const days = calendarDaysBetween(new Date(iso), now);
  if (days <= 0) return 'today';
  if (days === 1) return 'yesterday';
  return days < 7 ? 'week' : 'older';
};
