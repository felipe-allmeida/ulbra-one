import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { type AuditLog } from '../api';
import { useAuditLogs, useUsers } from '../queries';
import { P, usePermissions } from '../permissions';
import { dayEndIso, dayStartIso, daysAgoDate, fmtInstant } from '../datetime';
import { Drawer, Input, LoadingState, Pager, Panel, PageHeader, Select, ToolbarField } from '../ui';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';
import { UserAvatar } from '../user-avatar';
import {
  AUDIT_ENTITY_TYPES,
  actorDisplay,
  buildAuditDiff,
  entityLabel,
  fieldLabel,
  formatAuditValue,
  stampedActorId,
} from './audit-labels';

const PAGE_SIZE = 20;
/** Default window: the trail takes a row per write, so an unbounded first load would be the whole history. */
const DEFAULT_DAYS = 7;

/**
 * Audit trail browser (admin). Reads the append-only write trail of every module — what changed, on which
 * entity, by whom and when — with a drawer that renders each entry field by field.
 *
 * Two limits the screen is upfront about, both inherited from what the trail records: there is no company on
 * an entry, so this view is global and ignores the company switcher; and the actor is stamped as an id, which
 * only a reader with `admin:users` can see resolved to a name.
 */
export function AuditPage() {
  const { t } = useTranslation();
  // Naming the actor is a nicety, not a requirement: Controladoria holds audit:read without admin:users,
  // and mounting the users query for them would 403.
  const canListUsers = usePermissions().can(P.adminUsers);
  const users = useUsers({ enabled: canListUsers });

  const [from, setFrom] = useState(() => daysAgoDate(DEFAULT_DAYS));
  const [to, setTo] = useState('');
  const [entityType, setEntityType] = useState('');
  const [entityId, setEntityId] = useState('');
  // Holds what the reader picked/typed (a `/users` id, or a raw stamped id); converted at query time.
  const [actor, setActor] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<AuditLog | null>(null);

  /** Any filter change invalidates the page number — page 3 of the previous result set means nothing. */
  const onFilter =
    <T,>(set: (value: T) => void) =>
    (value: T) => {
      set(value);
      setPage(1);
    };

  const query = useAuditLogs({
    entityType: entityType || undefined,
    entityId: entityId.trim() || undefined,
    actorUserId: actor ? stampedActorId(actor) : undefined,
    from: dayStartIso(from),
    to: dayEndIso(to),
    page,
    pageSize: PAGE_SIZE,
  });

  const items = query.data?.items ?? [];
  const entityOptions = useMemo(
    () =>
      AUDIT_ENTITY_TYPES.map((type) => ({ type, label: entityLabel(type, t) })).sort((a, b) =>
        a.label.localeCompare(b.label, 'pt-BR'),
      ),
    [t],
  );

  return (
    <>
      <PageHeader title={t('audit.title')} sub={t('audit.subtitle')} />

      <div className='audit-filters'>
        <ToolbarField label={t('audit.filters.from')}>
          <Input
            type='date'
            aria-label={t('audit.filters.from')}
            value={from}
            onChange={(e) => onFilter(setFrom)(e.target.value)}
          />
        </ToolbarField>
        <ToolbarField label={t('audit.filters.to')}>
          <Input
            type='date'
            aria-label={t('audit.filters.to')}
            value={to}
            onChange={(e) => onFilter(setTo)(e.target.value)}
          />
        </ToolbarField>
        <ToolbarField label={t('audit.filters.entityType')}>
          <Select
            aria-label={t('audit.filters.entityType')}
            value={entityType}
            onChange={(e) => onFilter(setEntityType)(e.target.value)}
          >
            <option value=''>{t('audit.filters.all')}</option>
            {entityOptions.map((o) => (
              <option key={o.type} value={o.type}>
                {o.label}
              </option>
            ))}
          </Select>
        </ToolbarField>
        <ToolbarField label={t('audit.filters.entityId')}>
          <Input
            aria-label={t('audit.filters.entityId')}
            placeholder={t('audit.filters.entityIdPlaceholder')}
            value={entityId}
            onChange={(e) => onFilter(setEntityId)(e.target.value)}
          />
        </ToolbarField>
        <ToolbarField label={t('audit.filters.actor')}>
          {/* With admin:users the actor is picked by name; without it, only the stamped id can be typed. */}
          {canListUsers ? (
            <Select
              aria-label={t('audit.filters.actor')}
              value={actor}
              onChange={(e) => onFilter(setActor)(e.target.value)}
            >
              <option value=''>{t('audit.filters.anyActor')}</option>
              {(users.data ?? []).map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name}
                </option>
              ))}
            </Select>
          ) : (
            <Input
              aria-label={t('audit.filters.actor')}
              placeholder={t('audit.filters.actorIdPlaceholder')}
              value={actor}
              onChange={(e) => onFilter(setActor)(e.target.value)}
            />
          )}
        </ToolbarField>
      </div>

      <Panel title={t('audit.count', { count: query.data?.total ?? 0 })}>
        {query.isLoading ? (
          <LoadingState />
        ) : items.length === 0 ? (
          <div className='empty-state' style={{ padding: 24 }}>
            <div className='t'>{t('audit.empty')}</div>
          </div>
        ) : (
          <ResponsiveTable
            rows={items}
            columns={auditColumns(t, users.data)}
            rowKey={(log) => log.id}
            onRowClick={(log) => setSelected(log)}
          />
        )}
        <Pager page={query.data?.page ?? 1} totalPages={query.data?.totalPages ?? 1} onPage={setPage} />
      </Panel>

      {selected && <AuditDetailDrawer log={selected} users={users.data} onClose={() => setSelected(null)} />}
    </>
  );
}

/** Reuses the workflow status palette: a delete is the one entry that should stand out in a scan. */
function actionBadge(action: AuditLog['action']) {
  return action === 'Delete' ? 'badge-rejeitado' : action === 'Create' ? 'badge-aprovado' : 'badge-pendente';
}

/**
 * Ordem = a ordem já renderizada hoje. Quando ocorreu é a única `title` (mesmo papel usado no histórico
 * de pagamentos — a data é a identidade natural de uma linha de trilha). Ator e entidade são os dois
 * `meta` (contexto de relance); ação é a única badge → `status`. Id do registro pode ser um guid/prefixed
 * id longo e a contagem de alterações é secundária — ambos em `detail`.
 */
/** Just enough of a `/users` row to name and picture the actor. */
interface AuditActor {
  id: string;
  name: string;
  pictureUrl?: string | null;
}

/** The actor of one trail entry: photo when it resolves, initials otherwise. */
function ActorCell({ actorUserId, users }: { actorUserId: string; users: AuditActor[] | undefined }) {
  const { t } = useTranslation();
  const actor = actorDisplay(actorUserId, users, t);
  if (!actor.resolved) return actor.name;
  return (
    <span className='audit-actor'>
      <UserAvatar className='audit-actor-avatar' name={actor.name} pictureUrl={actor.pictureUrl} />
      {actor.name}
    </span>
  );
}

function auditColumns(t: TFunction, users: AuditActor[] | undefined): Column<AuditLog>[] {
  return [
    {
      key: 'occurredAt',
      header: t('audit.columns.occurredAt'),
      role: 'title',
      cell: (log) => <span className='t-mono'>{fmtInstant(log.occurredAt)}</span>,
    },
    {
      key: 'actor',
      header: t('audit.columns.actor'),
      role: 'meta',
      cell: (log) => <ActorCell actorUserId={log.actorUserId} users={users} />,
    },
    {
      key: 'action',
      header: t('audit.columns.action'),
      role: 'status',
      cell: (log) => (
        <span className={`badge-status ${actionBadge(log.action)}`}>{t(`audit.actions.${log.action}`)}</span>
      ),
    },
    {
      key: 'entity',
      header: t('audit.columns.entity'),
      role: 'meta',
      cell: (log) => entityLabel(log.entityType, t),
    },
    {
      key: 'entityId',
      header: t('audit.columns.entityId'),
      role: 'detail',
      cell: (log) => <span className='t-mono'>{log.primaryKey}</span>,
    },
    {
      key: 'changes',
      header: t('audit.columns.changes'),
      role: 'detail',
      cell: (log) => t('audit.changeCount', { count: buildAuditDiff(log).length }),
    },
  ];
}

/** One entry read field by field: what each column held before the write and what it holds after. */
function AuditDetailDrawer({
  log,
  users,
  onClose,
}: {
  log: AuditLog;
  users: AuditActor[] | undefined;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const diff = buildAuditDiff(log);

  return (
    <Drawer
      open
      onClose={onClose}
      width={620}
      title={t('audit.detail.title')}
      sub={`${t(`audit.actions.${log.action}`)} · ${entityLabel(log.entityType, t)} · ${fmtInstant(log.occurredAt)}`}
    >
      <dl className='detail-dl'>
        <dt>{t('audit.columns.actor')}</dt>
        <dd>
          <ActorCell actorUserId={log.actorUserId} users={users} />
        </dd>
        <dt>{t('audit.columns.entityId')}</dt>
        <dd>{log.primaryKey}</dd>
        <dt>{t('audit.detail.module')}</dt>
        <dd>{log.schema || t('audit.value.empty')}</dd>
        <dt>{t('audit.detail.table')}</dt>
        <dd>{log.tableName}</dd>
        <dt>{t('audit.detail.traceId')}</dt>
        <dd>{log.traceId ?? t('audit.value.empty')}</dd>
      </dl>

      {diff.length === 0 ? (
        <div className='empty-state' style={{ padding: 24 }}>
          <div className='t'>{t('audit.detail.noChanges')}</div>
        </div>
      ) : (
        <div className='drawer-table-scroll'>
          <ResponsiveTable
            rows={diff}
            // O campo é a única `title`; antes/depois são os dois `meta` (teto da cardinalidade) — texto
            // simples que trunca com reticências visíveis quando comprido, nunca em silêncio.
            columns={[
              { key: 'field', header: t('audit.detail.field'), role: 'title', cell: (l) => fieldLabel(l.column, t) },
              {
                key: 'before',
                header: t('audit.detail.before'),
                role: 'meta',
                cell: (l) => formatAuditValue(l.before, t),
              },
              {
                key: 'after',
                header: t('audit.detail.after'),
                role: 'meta',
                cell: (l) => formatAuditValue(l.after, t),
              },
            ]}
            rowKey={(l) => l.column}
          />
        </div>
      )}

      {/* Escape hatch: the curated field labels cover the common columns, never every one of them. */}
      <details className='audit-raw'>
        <summary>{t('audit.detail.raw')}</summary>
        <pre>{JSON.stringify({ oldValues: log.oldValues, newValues: log.newValues }, null, 2)}</pre>
      </details>
    </Drawer>
  );
}
