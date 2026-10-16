import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { type AuditLog } from '../api';
import { useAuditLogs, useUsers } from '../queries';
import { P, usePermissions } from '../permissions';
import { dayEndIso, dayStartIso, daysAgoDate, fmtInstant } from '../datetime';
import { Drawer, Input, LoadingState, Pager, Panel, PageHeader, Select, ToolbarField } from '../ui';
import {
  AUDIT_ENTITY_TYPES,
  actorLabel,
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
          <table>
            <thead>
              <tr>
                <th>{t('audit.columns.occurredAt')}</th>
                <th>{t('audit.columns.actor')}</th>
                <th>{t('audit.columns.action')}</th>
                <th>{t('audit.columns.entity')}</th>
                <th>{t('audit.columns.entityId')}</th>
                <th>{t('audit.columns.changes')}</th>
              </tr>
            </thead>
            <tbody>
              {items.map((log) => (
                <tr key={log.id} className='row-click' onClick={() => setSelected(log)}>
                  <td className='t-mono'>{fmtInstant(log.occurredAt)}</td>
                  <td>{actorLabel(log.actorUserId, users.data, t)}</td>
                  <td>
                    <span className={`badge-status ${actionBadge(log.action)}`}>
                      {t(`audit.actions.${log.action}`)}
                    </span>
                  </td>
                  <td>{entityLabel(log.entityType, t)}</td>
                  <td className='t-mono'>{log.primaryKey}</td>
                  <td>{t('audit.changeCount', { count: buildAuditDiff(log).length })}</td>
                </tr>
              ))}
            </tbody>
          </table>
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

/** One entry read field by field: what each column held before the write and what it holds after. */
function AuditDetailDrawer({
  log,
  users,
  onClose,
}: {
  log: AuditLog;
  users: { id: string; name: string }[] | undefined;
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
        <dd>{actorLabel(log.actorUserId, users, t)}</dd>
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
          <table>
            <thead>
              <tr>
                <th>{t('audit.detail.field')}</th>
                <th>{t('audit.detail.before')}</th>
                <th>{t('audit.detail.after')}</th>
              </tr>
            </thead>
            <tbody>
              {diff.map((line) => (
                <tr key={line.column}>
                  <td>{fieldLabel(line.column, t)}</td>
                  <td>{formatAuditValue(line.before, t)}</td>
                  <td>{formatAuditValue(line.after, t)}</td>
                </tr>
              ))}
            </tbody>
          </table>
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
