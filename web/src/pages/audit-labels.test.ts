import { describe, it, expect } from 'vitest';
import '../i18n';
import i18next from 'i18next';
import type { TFunction } from 'i18next';
import {
  AUDIT_ENTITY_TYPES,
  actorLabel,
  buildAuditDiff,
  entityLabel,
  fieldLabel,
  formatAuditValue,
  stampedActorId,
} from './audit-labels';

const t = i18next.t.bind(i18next) as unknown as TFunction;

describe('buildAuditDiff', () => {
  it('lists one line per affected column on an update, with before and after', () => {
    const diff = buildAuditDiff({
      action: 'Update',
      oldValues: JSON.stringify({ Amount: 100, Status: 'Pending', Description: 'same' }),
      newValues: JSON.stringify({ Amount: 250, Status: 'Approved', Description: 'same' }),
      affectedColumns: JSON.stringify(['Amount', 'Status']),
    });

    expect(diff).toEqual([
      { column: 'Amount', before: 100, after: 250 },
      { column: 'Status', before: 'Pending', after: 'Approved' },
    ]);
  });

  it('falls back to the union of both payloads when affectedColumns is absent', () => {
    const diff = buildAuditDiff({
      action: 'Update',
      oldValues: JSON.stringify({ Amount: 100 }),
      newValues: JSON.stringify({ Amount: 250, Note: 'novo' }),
      affectedColumns: null,
    });

    expect(diff.map((l) => l.column)).toEqual(['Amount', 'Note']);
    expect(diff[1]).toEqual({ column: 'Note', before: undefined, after: 'novo' });
  });

  it('reads an insert from newValues alone — there is nothing before it', () => {
    const diff = buildAuditDiff({
      action: 'Create',
      oldValues: null,
      newValues: JSON.stringify({ Amount: 100, Status: 'Pending' }),
      affectedColumns: null,
    });

    expect(diff).toEqual([
      { column: 'Amount', before: undefined, after: 100 },
      { column: 'Status', before: undefined, after: 'Pending' },
    ]);
  });

  it('reads a delete from oldValues alone', () => {
    const diff = buildAuditDiff({
      action: 'Delete',
      oldValues: JSON.stringify({ Amount: 100 }),
      newValues: null,
      affectedColumns: null,
    });

    expect(diff).toEqual([{ column: 'Amount', before: 100, after: undefined }]);
  });

  it('returns an empty diff instead of throwing on a malformed payload', () => {
    expect(buildAuditDiff({ action: 'Update', oldValues: 'not json', newValues: '{', affectedColumns: '[' })).toEqual(
      [],
    );
  });
});

describe('formatAuditValue', () => {
  it('renders a date-time as pt-BR, not as raw ISO', () => {
    // No offset in the input: JS parses it as local time, so the expectation holds in any timezone.
    expect(formatAuditValue('2026-08-20T14:03:11', t)).toBe('20/08/2026 14:03');
  });

  it('renders a date-only value without a time', () => {
    expect(formatAuditValue('2026-08-20', t)).toBe('20/08/2026');
  });

  it('renders booleans and empty values in pt-BR', () => {
    expect(formatAuditValue(true, t)).toBe('Sim');
    expect(formatAuditValue(false, t)).toBe('Não');
    expect(formatAuditValue(null, t)).toBe('—');
    expect(formatAuditValue('', t)).toBe('—');
  });

  it('formats numbers in pt-BR — a money column reads as an amount, not as a raw literal', () => {
    expect(formatAuditValue(12500, t)).toBe('12.500');
    expect(formatAuditValue(13750.5, t)).toBe('13.750,5');
  });

  it('keeps plain scalars as text and serializes nested payloads', () => {
    expect(formatAuditValue('Pendente', t)).toBe('Pendente');
    expect(formatAuditValue({ id: 1 }, t)).toBe('{"id":1}');
  });
});

describe('entityLabel / fieldLabel', () => {
  it('translates the curated entity types', () => {
    expect(entityLabel('Payable', t)).toBe('Conta a pagar');
    expect(AUDIT_ENTITY_TYPES).toContain('Payable');
  });

  it('falls back to the raw CLR name for an entity outside the curated list', () => {
    expect(entityLabel('SomethingNew', t)).toBe('SomethingNew');
  });

  it('translates common column names and falls back to the raw column', () => {
    expect(fieldLabel('Amount', t)).toBe('Valor');
    expect(fieldLabel('WeirdColumn', t)).toBe('WeirdColumn');
  });
});

describe('actorLabel', () => {
  // The trail stamps the raw Guid from the token's `sub`, while /users returns the prefixed id — so a
  // literal comparison never matches and every row would read as an unresolved Guid.
  const users = [
    { id: 'user_3fa85f6457174219973000000000000a', name: 'Ana Contábil' },
    { id: 'user_00000000000000000000000000000001', name: 'Bruno Financeiro' },
  ];

  it('resolves the raw Guid stamped in the trail to the user name', () => {
    expect(actorLabel('3fa85f64-5717-4219-9730-00000000000a', users, t)).toBe('Ana Contábil');
  });

  it('names the system actor for writes with no authenticated user', () => {
    expect(actorLabel('system', users, t)).toBe('Sistema');
  });

  it('keeps the raw id when the user list is unavailable (no admin:users permission)', () => {
    expect(actorLabel('3fa85f64-5717-4219-9730-00000000000a', undefined, t)).toBe(
      '3fa85f64-5717-4219-9730-00000000000a',
    );
  });

  it('keeps the raw id when no user matches', () => {
    expect(actorLabel('99999999-9999-9999-9999-999999999999', users, t)).toBe('99999999-9999-9999-9999-999999999999');
  });
});

describe('stampedActorId', () => {
  it('converts the prefixed user id into the raw Guid the trail stamped', () => {
    expect(stampedActorId('user_3fa85f6457174219973000000000000a')).toBe('3fa85f64-5717-4219-9730-00000000000a');
  });

  it('leaves an id it cannot recognize untouched', () => {
    expect(stampedActorId('system')).toBe('system');
  });
});
