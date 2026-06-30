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
