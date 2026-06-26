import { type PayableListItem } from '../../api';

export type PeriodKey = 'today' | 'thisWeek' | 'nextWeek' | 'future';

export interface PeriodGroup {
  key: PeriodKey;
  items: PayableListItem[];
  subtotal: number;
}

const ORDER: PeriodKey[] = ['today', 'thisWeek', 'nextWeek', 'future'];

/** Days from `from` (date-only) to the coming Sunday (0 if already Sunday). */
function daysToSunday(from: Date): number {
  return (7 - from.getDay()) % 7;
}

function bucketOf(dueDate: string, now: Date): PeriodKey {
  const [y, m, d] = dueDate.split('-').map(Number);
  const due = Date.UTC(y, m - 1, d);
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  if (due <= today) return 'today';
  const endOfThisWeek = today + (daysToSunday(now) + 0) * 86_400_000;
  if (due <= endOfThisWeek) return 'thisWeek';
  const endOfNextWeek = endOfThisWeek + 7 * 86_400_000;
  if (due <= endOfNextWeek) return 'nextWeek';
  return 'future';
}

export function groupByPeriod(items: PayableListItem[], now: Date = new Date()): PeriodGroup[] {
  const map = new Map<PeriodKey, PayableListItem[]>();
  for (const item of items) {
    const key = bucketOf(item.dueDate, now);
    (map.get(key) ?? map.set(key, []).get(key)!).push(item);
  }
  return ORDER.filter((k) => map.has(k)).map((key) => {
    const groupItems = map.get(key)!;
    return { key, items: groupItems, subtotal: groupItems.reduce((s, i) => s + i.amount, 0) };
  });
}
