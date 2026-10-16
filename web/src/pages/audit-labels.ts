import type { TFunction } from 'i18next';
import type { AuditAction } from '../api';
import { fmtInstant } from '../datetime';

/**
 * Reading aids for the audit trail. The trail is captured at the persistence layer, so it speaks in CLR
 * type names and EF property names (PascalCase) — these helpers turn that into pt-BR without pretending to
 * know every entity: anything outside the curated maps falls back to the raw name instead of hiding it.
 */

/** Entity types offered in the filter, in display order. Not exhaustive — the trail records every entity. */
export const AUDIT_ENTITY_TYPES = [
  'Payable',
  'Payment',
  'Receivable',
  'Receipt',
  'Requisition',
  'BudgetAdjustment',
  'Project',
  'VendorEntity',
  'CustomerEntity',
  'EmployeeEntity',
  'User',
  'Role',
  'Company',
  'Unit',
  'CostCenter',
  'CostCenterUnitBudget',
  'BankAccount',
  'JournalEntry',
  'LedgerAccount',
  'ExpenseCategory',
  'WithholdingRule',
  'Remittance',
  'AccountingPeriod',
] as const;

/** pt-BR name of the audited entity; the raw CLR name when it is not one of the curated types. */
export const entityLabel = (entityType: string, t: TFunction): string =>
  t(`audit.entities.${entityType}`, { defaultValue: entityType });

/** pt-BR name of the changed column; the raw property name when it is not a well-known one. */
export const fieldLabel = (column: string, t: TFunction): string =>
  t(`audit.fields.${column}`, { defaultValue: column });

/** One changed column: `before` is undefined on an insert, `after` on a delete. */
export interface AuditDiffLine {
  column: string;
  before: unknown;
  after: unknown;
}

interface AuditPayload {
  action: AuditAction;
  oldValues?: string | null;
  newValues?: string | null;
  affectedColumns?: string | null;
}

/** Parses a payload the trail stored as JSON text. Malformed text reads as "nothing", never as a throw. */
function parseObject(json: string | null | undefined): Record<string, unknown> {
  if (!json) return {};
  try {
    const parsed: unknown = JSON.parse(json);
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : {};
  } catch {
    return {};
  }
}

function parseColumns(json: string | null | undefined): string[] | null {
  if (!json) return null;
  try {
    const parsed: unknown = JSON.parse(json);
    return Array.isArray(parsed) ? parsed.map(String) : null;
  } catch {
    return null;
  }
}

/**
 * Field-by-field view of one entry. `affectedColumns` drives the order when the trail recorded it (updates);
 * otherwise the columns are the union of both payloads, old first, so an insert/delete reads as a plain list.
 */
export function buildAuditDiff(log: AuditPayload): AuditDiffLine[] {
  const before = parseObject(log.oldValues);
  const after = parseObject(log.newValues);
  const columns = parseColumns(log.affectedColumns) ?? [...new Set([...Object.keys(before), ...Object.keys(after)])];
  return columns.map((column) => ({ column, before: before[column], after: after[column] }));
}

const DATE_ONLY = /^(\d{4})-(\d{2})-(\d{2})$/;
const DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/;

/** Human-readable rendering of a single audited value. Anything unrecognized falls back to its JSON form. */
export function formatAuditValue(value: unknown, t: TFunction): string {
  if (value === null || value === undefined || value === '') return t('audit.value.empty');
  if (typeof value === 'boolean') return t(value ? 'audit.value.yes' : 'audit.value.no');
  // pt-BR grouping: most numeric columns in the trail are money, and `13750.5` does not read as an amount.
  // No fixed decimals — the trail has integer columns too (quantities, counts).
  if (typeof value === 'number') return value.toLocaleString('pt-BR', { maximumFractionDigits: 6 });
  if (typeof value === 'string') {
    const dateOnly = DATE_ONLY.exec(value);
    // Date-only is formatted from its own parts: `new Date('2026-08-20')` is UTC midnight and would
    // slide to the previous day west of Greenwich.
    if (dateOnly) return `${dateOnly[3]}/${dateOnly[2]}/${dateOnly[1]}`;
    if (DATE_TIME.test(value)) return fmtInstant(value);
    return value;
  }
  return JSON.stringify(value);
}

/** Minimal shape needed to name an actor — `AdminUser` satisfies it. */
interface ActorCandidate {
  id: string;
  name: string;
  pictureUrl?: string | null;
}

/** How an actor is shown on the trail: a label plus the photo to paint, when there is one. */
export interface ActorDisplay {
  name: string;
  pictureUrl: string | null;
  /** Whether the id matched a real user. False for the system actor and for an id that can't be named —
   * neither is a person, so neither gets an avatar (initials of a Guid say nothing). */
  resolved: boolean;
}

/**
 * Ids compared bare: the trail stamps the token's `sub` (raw Guid with hyphens) while `/users` returns the
 * prefixed id (`user_{guid:N}`), so a literal comparison would never match.
 */
const bareId = (id: string) =>
  id
    .replace(/^[a-z]+_/, '')
    .replace(/-/g, '')
    .toLowerCase();

/**
 * Who made the change. Falls back to the stamped id (and no photo) when the actor can't be named — either the
 * caller lacks `admin:users` (no list to match against) or the user no longer exists.
 */
export function actorDisplay(
  actorUserId: string,
  users: readonly ActorCandidate[] | undefined,
  t: TFunction,
): ActorDisplay {
  if (actorUserId === 'system') return { name: t('audit.systemActor'), pictureUrl: null, resolved: false };
  const bare = bareId(actorUserId);
  const match = users?.find((u) => bareId(u.id) === bare);
  return { name: match?.name ?? actorUserId, pictureUrl: match?.pictureUrl ?? null, resolved: !!match };
}

/**
 * `/users` id (`user_{guid:N}`) → the id the trail actually stamped. The audit interceptor writes the token's
 * `sub`, which is `Guid.ToString()` — hyphenated — so filtering by the prefixed id would never match.
 * Anything that is not a 32-hex id (e.g. `system`) is passed through untouched.
 */
export function stampedActorId(userId: string): string {
  const bare = bareId(userId);
  if (!/^[0-9a-f]{32}$/.test(bare)) return userId;
  return `${bare.slice(0, 8)}-${bare.slice(8, 12)}-${bare.slice(12, 16)}-${bare.slice(16, 20)}-${bare.slice(20)}`;
}
