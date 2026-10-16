import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import '../i18n'; // pt-BR labels.
import { setViewportMatches } from '../test/setup';
import { type AuditLog } from '../api';
import type * as Permissions from '../permissions';

// Naming the actor needs /users, which requires admin:users — Controladoria reaches this screen
// (audit:read) without it and must still see the trail.
const { permState } = vi.hoisted(() => ({ permState: { canAdminUsers: true } }));
vi.mock('../permissions', async (orig) => ({
  ...(await orig<typeof Permissions>()),
  usePermissions: () => ({
    can: (p: string) => (p === 'admin:users' ? permState.canAdminUsers : false),
    canAny: () => false,
    me: null,
    isAdmin: false,
  }),
}));

const { mockAuditLogs, mockUsers } = vi.hoisted(() => ({ mockAuditLogs: vi.fn(), mockUsers: vi.fn() }));
vi.mock('../queries', () => ({ useAuditLogs: mockAuditLogs, useUsers: mockUsers }));

import { AuditPage } from './audit';

const ACTOR = '3fa85f64-5717-4219-9730-00000000000a';
const USERS = [{ id: 'user_3fa85f6457174219973000000000000a', name: 'Ana Contábil' }];

const log = (over: Partial<AuditLog> = {}): AuditLog => ({
  id: 'audit_log_1',
  // No offset: parsed as local time, so the rendered value is the same in any timezone.
  occurredAt: '2026-08-20T14:03:11',
  traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
  actorUserId: ACTOR,
  action: 'Update',
  entityType: 'Payable',
  schema: 'finance',
  tableName: 'payables',
  primaryKey: 'pay_1',
  oldValues: JSON.stringify({ Amount: 100 }),
  newValues: JSON.stringify({ Amount: 250 }),
  affectedColumns: JSON.stringify(['Amount']),
  ...over,
});

const pageOf = (items: AuditLog[]) => ({
  data: { items, page: 1, pageSize: 20, total: items.length, totalPages: 1 },
  isLoading: false,
});

/** Filters of the most recent useAuditLogs call. */
const lastQuery = () => mockAuditLogs.mock.calls.at(-1)![0] as Record<string, unknown>;

describe('AuditPage', () => {
  beforeEach(() => {
    permState.canAdminUsers = true;
    mockAuditLogs.mockReset().mockReturnValue(pageOf([log()]));
    mockUsers.mockReset().mockReturnValue({ data: USERS });
  });

  it('renders one row per entry, in pt-BR, with the actor named', () => {
    render(<AuditPage />);

    // Scoped to the table: the actor's name also appears in the filter's actor picker.
    const table = within(screen.getByRole('table'));
    expect(table.getByText('20/08/2026 14:03')).toBeInTheDocument();
    expect(table.getByText('Ana Contábil')).toBeInTheDocument();
    expect(table.getByText('Alterou')).toBeInTheDocument();
    expect(table.getByText('Conta a pagar')).toBeInTheDocument();
    expect(table.getByText('pay_1')).toBeInTheDocument();
    expect(table.getByText('1 campo')).toBeInTheDocument();
  });

  it('shows the stamped id when the caller cannot list users (no admin:users)', () => {
    permState.canAdminUsers = false;
    mockUsers.mockReturnValue({ data: undefined });

    render(<AuditPage />);

    expect(screen.getByText(ACTOR)).toBeInTheDocument();
    // The users query must not even be attempted — it would 403.
    expect(mockUsers).toHaveBeenCalledWith({ enabled: false });
  });

  it('asks for the last 7 days before any filter is touched', () => {
    render(<AuditPage />);

    const expected = new Date();
    expected.setDate(expected.getDate() - 7);
    const from = new Date(lastQuery().from as string);
    expect([from.getFullYear(), from.getMonth(), from.getDate()]).toEqual([
      expected.getFullYear(),
      expected.getMonth(),
      expected.getDate(),
    ]);
    // Midnight, not "now minus 7×24h": the whole first day must be inside the window.
    expect([from.getHours(), from.getMinutes()]).toEqual([0, 0]);
  });

  it('filters by entity type using the CLR name behind the pt-BR label', () => {
    render(<AuditPage />);

    fireEvent.change(screen.getByLabelText('Entidade'), { target: { value: 'Requisition' } });

    expect(lastQuery().entityType).toBe('Requisition');
  });

  it('filters by the record id', () => {
    render(<AuditPage />);

    fireEvent.change(screen.getByLabelText('Id do registro'), { target: { value: 'pay_9' } });

    expect(lastQuery().entityId).toBe('pay_9');
  });

  it('filters by actor using the Guid the trail stamped, not the prefixed id', () => {
    render(<AuditPage />);

    fireEvent.change(screen.getByLabelText('Autor'), { target: { value: 'user_3fa85f6457174219973000000000000a' } });

    expect(lastQuery().actorUserId).toBe(ACTOR);
  });

  it('opens the drawer with the change read field by field', () => {
    render(<AuditPage />);

    fireEvent.click(screen.getByText('pay_1'));

    expect(screen.getByText('Detalhe da alteração')).toBeInTheDocument();
    expect(screen.getByText('Valor')).toBeInTheDocument();
    expect(screen.getByText('100')).toBeInTheDocument();
    expect(screen.getByText('250')).toBeInTheDocument();
    expect(screen.getByText('4bf92f3577b34da6a3ce929d0e0e4736')).toBeInTheDocument();
  });

  it('shows an empty state instead of a bare table when nothing matches', () => {
    mockAuditLogs.mockReturnValue(pageOf([]));

    render(<AuditPage />);

    expect(screen.getByText('Nenhum registro de auditoria para os filtros escolhidos.')).toBeInTheDocument();
  });
});

describe('AuditPage no telefone', () => {
  beforeEach(() => {
    permState.canAdminUsers = true;
    mockAuditLogs.mockReset().mockReturnValue(pageOf([log()]));
    mockUsers.mockReset().mockReturnValue({ data: USERS });
    setViewportMatches(true);
  });

  it('mostra um card por entrada, sem <table>', () => {
    render(<AuditPage />);
    // Escopado ao card: "Ana Contábil" também aparece no seletor de autor do filtro.
    const cards = within(document.querySelector('.rt-cards')!);
    expect(cards.getByText('Ana Contábil')).toBeInTheDocument();
    expect(document.querySelector('table')).not.toBeInTheDocument();
  });

  it('esconde o id do registro até expandir o card', () => {
    render(<AuditPage />);
    // `entityId` é uma coluna `detail` — não aparece no card fechado.
    expect(screen.queryByText('pay_1')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /expandir/i }));
    expect(screen.getByText('pay_1')).toBeInTheDocument();
  });
});
