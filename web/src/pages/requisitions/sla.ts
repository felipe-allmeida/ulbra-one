/**
 * SLA de estágio (AEL-790): 24h ÚTEIS desde a entrada no estágio (`statusChangedAt`),
 * excluindo fins de semana + feriados nacionais BR (fixos + móveis via Páscoa).
 * Aplica-se aos estágios de Triagem e Aprovação.
 */

const FIXED_HOLIDAYS = ['01-01', '04-21', '05-01', '09-07', '10-12', '11-02', '11-15', '11-20', '12-25'];

/** Domingo de Páscoa (algoritmo de Computus / Meeus) para o ano. */
function easter(year: number): Date {
  const a = year % 19;
  const b = Math.floor(year / 100);
  const c = year % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const month = Math.floor((h + l - 7 * m + 114) / 31);
  const day = ((h + l - 7 * m + 114) % 31) + 1;
  return new Date(year, month - 1, day);
}

const mmdd = (d: Date) => `${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

function movableHolidays(year: number): string[] {
  const e = easter(year);
  const shifted = (days: number) => {
    const d = new Date(e);
    d.setDate(d.getDate() + days);
    return mmdd(d);
  };
  // Carnaval (seg/ter), Sexta-feira Santa, Corpus Christi.
  return [shifted(-48), shifted(-47), shifted(-2), shifted(60)];
}

const holidayCache = new Map<number, Set<string>>();
function holidaysFor(year: number): Set<string> {
  let s = holidayCache.get(year);
  if (!s) {
    s = new Set([...FIXED_HOLIDAYS, ...movableHolidays(year)]);
    holidayCache.set(year, s);
  }
  return s;
}

function isBusinessDay(d: Date): boolean {
  const dow = d.getDay();
  if (dow === 0 || dow === 6) return false; // domingo/sábado
  return !holidaysFor(d.getFullYear()).has(mmdd(d));
}

/** Horas úteis decorridas entre `start` e `now` (conta cada hora que cai em dia útil). */
export function businessHoursElapsed(start: Date, now: Date): number {
  if (now <= start) return 0;
  let hours = 0;
  const cur = new Date(start);
  cur.setMinutes(0, 0, 0);
  let guard = 0;
  while (cur < now && guard++ < 24 * 400) {
    if (isBusinessDay(cur)) hours++;
    cur.setHours(cur.getHours() + 1);
  }
  return hours;
}

export interface StageSla {
  elapsed: number;
  remaining: number;
  overdue: boolean;
  tone: 'ok' | 'warn' | 'danger';
}

/** Estado do SLA a partir da entrada no estágio (24h úteis por padrão). */
export function stageSla(statusChangedAtIso: string, limitHours = 24, now: Date = new Date()): StageSla | null {
  const start = new Date(statusChangedAtIso);
  if (Number.isNaN(start.getTime())) return null;
  const elapsed = businessHoursElapsed(start, now);
  const remaining = limitHours - elapsed;
  return {
    elapsed,
    remaining,
    overdue: remaining < 0,
    tone: remaining < 0 ? 'danger' : remaining <= 4 ? 'warn' : 'ok',
  };
}
