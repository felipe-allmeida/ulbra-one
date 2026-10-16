import { type PayableListItem } from '../../api';

export type PaymentRailBucket = 'manual' | 'held' | 'nextSweep' | 'scheduled' | 'inRemittance' | 'paid';

export interface RailLine {
  installmentStatus: string;
  isHeld: boolean;
  rail: 'Remittance' | 'Manual';
  manualReason: string | null;
  scheduledPaymentDate: string; // ISO yyyy-mm-dd
  remittanceSequence: number | null;
}

/**
 * Derives the payment-rail bucket for display purposes.
 * Precedence (exact, order matters):
 *   Paid → inRemittance (installmentStatus==='InRemittance') → manual (rail==='Manual')
 *   → held (isHeld) → then scheduledPaymentDate <= todayIso ? 'nextSweep' : 'scheduled'
 */
export function railBucket(l: RailLine, todayIso: string): PaymentRailBucket {
  if (l.installmentStatus === 'Paid') return 'paid';
  if (l.installmentStatus === 'InRemittance') return 'inRemittance';
  if (l.rail === 'Manual') return 'manual';
  if (l.isHeld) return 'held';
  return l.scheduledPaymentDate <= todayIso ? 'nextSweep' : 'scheduled';
}

/**
 * Converts a PayableListItem into a RailLine-compatible shape. Shared by the payables payment tab
 * and the treasury queue so the two surfaces never drift on bucket semantics.
 * The `rail` and `manualReason` fields are computed by the list endpoint (batched,
 * via RemittanceEligibility.Evaluate) — no client-side heuristic needed.
 */
export function toRailLine(item: PayableListItem): RailLine {
  return {
    installmentStatus: item.installmentStatus,
    isHeld: item.isHeld ?? false,
    rail: (item.rail as 'Remittance' | 'Manual') ?? 'Manual',
    manualReason: item.manualReason ?? null,
    scheduledPaymentDate: item.scheduledPaymentDate ?? item.dueDate,
    remittanceSequence: item.remittanceSequence ?? null,
  };
}

export function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}
