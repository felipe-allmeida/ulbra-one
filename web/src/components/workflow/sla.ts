/** Calendar-day difference from `now` to `dueDate` (YYYY-MM-DD). Positive = future, 0 = today, negative = overdue. */
export function daysUntil(dueDate: string, now: Date = new Date()): number {
  const [y, m, d] = dueDate.split('-').map(Number);
  const due = Date.UTC(y, m - 1, d);
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  return Math.round((due - today) / 86_400_000);
}

/** Urgency tone for a due-date chip. >7d safe, 3–7d warning, <3d or overdue danger. */
export function dueTone(days: number): 'ok' | 'warn' | 'danger' {
  if (days > 7) return 'ok';
  if (days >= 3) return 'warn';
  return 'danger';
}
