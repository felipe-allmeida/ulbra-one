/** The two views of the Livro Razão, surfaced as tabs. `spreadsheet` (planilha — a single flat table)
 *  is the primary view; `accounts` is the per-account panel view. Persisted in the URL as `?tab=`. */
export type LedgerTabKey = 'spreadsheet' | 'accounts';

export const TAB_ORDER: LedgerTabKey[] = ['spreadsheet', 'accounts'];
export const DEFAULT_TAB: LedgerTabKey = 'spreadsheet';

export function isTabKey(v: string | null): v is LedgerTabKey {
  return v != null && (TAB_ORDER as string[]).includes(v);
}
