import { type PayableListItem, type PaymentQueueSummary } from '../../api';

/** One rendered group of the payment queue: the overdue bucket or a single due date. */
export interface QueueGroup {
  /** 'overdue' for the past-due bucket, else the ISO yyyy-mm-dd due date. */
  key: string;
  items: PayableListItem[];
}

/**
 * Groups the CURRENT PAGE's rows for display: everything overdue first (single bucket), then one
 * group per due date ascending. Counts/totals shown in headers come from the queue summary, not
 * from these page-local groups.
 */
export function groupQueueByDueDate(items: PayableListItem[], today: string): QueueGroup[] {
  const overdue: PayableListItem[] = [];
  const byDay = new Map<string, PayableListItem[]>();
  for (const item of items) {
    if (item.dueDate < today) {
      overdue.push(item);
    } else {
      (byDay.get(item.dueDate) ?? byDay.set(item.dueDate, []).get(item.dueDate)!).push(item);
    }
  }
  const groups: QueueGroup[] = [];
  if (overdue.length > 0) groups.push({ key: 'overdue', items: overdue });
  for (const [date, dayItems] of Array.from(byDay.entries()).sort(([a], [b]) => a.localeCompare(b))) {
    groups.push({ key: date, items: dayItems });
  }
  return groups;
}

/**
 * Boleto de arrecadação/convênio: 48-digit line starting with '8' (mirrors the backend's
 * BoletoBarcode.IsConvenio split — convênio rides CNAB Segment O instead of J).
 */
export function isAgreementBoleto(digitableLine: string | null | undefined): boolean {
  if (!digitableLine) return false;
  const digits = digitableLine.replace(/\D/g, '');
  return digits.length === 48 && digits.startsWith('8');
}

/** Formats a digits-only CNPJ/CPF for display; returns the input unchanged when unrecognized. */
export function formatVendorDocument(doc: string | null | undefined): string {
  const digits = (doc ?? '').replace(/\D/g, '');
  if (digits.length === 14)
    return `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5, 8)}/${digits.slice(8, 12)}-${digits.slice(12)}`;
  if (digits.length === 11)
    return `${digits.slice(0, 3)}.${digits.slice(3, 6)}.${digits.slice(6, 9)}-${digits.slice(9)}`;
  return doc ?? '';
}

/** "Competência" column: MM/yyyy of the payable's issue date (accounting competence). */
export function competencyLabel(issueDate: string | null | undefined): string {
  if (!issueDate || issueDate.length < 7) return '—';
  const [year, month] = issueDate.split('-');
  return `${month}/${year}`;
}

/** "Código" column: the fiscal document number when present, else a short payable reference. */
export function queueRowNumber(item: Pick<PayableListItem, 'documentNumber' | 'payableId'>): string {
  if (item.documentNumber) return `#${item.documentNumber}`;
  const id = item.payableId ?? '';
  return `#${id.slice(-6).toUpperCase()}`;
}

/** Truncates a linha digitável for the "entrega via" cell (start…end, mono). */
export function truncateDigitableLine(line: string): string {
  const digits = line.replace(/\D/g, '');
  if (digits.length <= 16) return digits;
  return `${digits.slice(0, 12)}…${digits.slice(-8)}`;
}

/** Sums the summary's day buckets from `from` (exclusive of overdue) up to `to`, both ISO dates. */
export function summaryWindowNet(summary: PaymentQueueSummary, from: string, to: string): number {
  return summary.groups.filter((g) => g.dueDate >= from && g.dueDate <= to).reduce((s, g) => s + g.netTotal, 0);
}

/** ISO date arithmetic in UTC (safe for yyyy-mm-dd keys). */
export function addDaysIso(iso: string, days: number): string {
  const d = new Date(`${iso}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}
