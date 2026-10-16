import { Fragment, type KeyboardEvent, type MouseEvent, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Bell, Check } from 'lucide-react';
import { type Notification } from '../api';
import {
  useMarkAllNotificationsRead,
  useMarkNotificationRead,
  useNotifications,
  useUnreadNotificationsCount,
} from '../queries';
import { useOriginNav } from '../nav';
import { dayBucket, fmtDateTime, fmtInstantDate, relativeTime } from '../datetime';
import { notificationMeta } from './notifications-catalog';
import { Button, cx, EmptyState, PageHeader, Pager, LoadingState } from '../ui';

/** Deep-link to the originating entity, derived from the notification type + referenceId (typed id). */
function routeFor(n: Notification): string | null {
  if (!n.referenceId) return null;
  if (n.type.startsWith('Requisition')) return `/app/requisitions/${n.referenceId}`;
  if (n.type.startsWith('BudgetAdjustment')) return `/app/budget-adjustments/${n.referenceId}`;
  return null;
}

export function NotificacoesPage() {
  const { t } = useTranslation();
  const openDetail = useOriginNav();
  const [page, setPage] = useState(1);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const { data, isLoading } = useNotifications(page, unreadOnly);
  const unreadCount = useUnreadNotificationsCount().data?.count ?? 0;
  const markRead = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();
  const items = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;

  const onOpen = (n: Notification) => {
    if (!n.readAt) markRead.mutate(n.id);
    const route = routeFor(n);
    if (route) void openDetail(route);
  };

  return (
    <div>
      <PageHeader
        title={t('notifications.title')}
        sub={
          unreadCount > 0
            ? `${t('notifications.unreadCount', { count: unreadCount })} · ${t('notifications.subtitle')}`
            : t('notifications.subtitle')
        }
      >
        <label className='ntf-filter'>
          <input
            type='checkbox'
            checked={unreadOnly}
            onChange={(e) => {
              setPage(1);
              setUnreadOnly(e.target.checked);
            }}
          />
          <span>{t('notifications.unreadOnly')}</span>
        </label>
        <Button onClick={() => markAll.mutate()} disabled={markAll.isPending || unreadCount === 0}>
          {t('notifications.markAllRead')}
        </Button>
      </PageHeader>
      {/* Bare .panel rather than <Panel>: the panel header would only repeat the page title. */}
      <div className='panel'>
        {isLoading ? (
          <LoadingState />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<Bell />}
            title={t(unreadOnly ? 'notifications.emptyUnread' : 'notifications.empty')}
            sub={t(unreadOnly ? 'notifications.emptyUnreadDescription' : 'notifications.emptyDescription')}
          />
        ) : (
          <ul className='ntf-list'>
            {items.map((n, i) => {
              // The list arrives newest-first, so a day header goes wherever the bucket changes.
              const bucket = dayBucket(n.occurredAt);
              const opensBucket = i === 0 || dayBucket(items[i - 1].occurredAt) !== bucket;
              return (
                <Fragment key={n.id}>
                  {opensBucket && <li className='ntf-day'>{t(`notifications.days.${bucket}`)}</li>}
                  <NotificationRow n={n} onOpen={onOpen} onMarkRead={(id) => markRead.mutate(id)} />
                </Fragment>
              );
            })}
          </ul>
        )}
      </div>
      <Pager page={page} totalPages={totalPages} onPage={setPage} />
    </div>
  );
}

function NotificationRow({
  n,
  onOpen,
  onMarkRead,
}: {
  n: Notification;
  onOpen: (n: Notification) => void;
  onMarkRead: (id: string) => void;
}) {
  const { t } = useTranslation();
  const { icon: Icon, tone, team } = notificationMeta(n.type);
  const clickable = routeFor(n) !== null;
  const unread = !n.readAt;

  const onKeyOpen = (e: KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      onOpen(n);
    }
  };
  // Marking read must not also navigate — the row's own onClick would otherwise fire and take the
  // reader away from the inbox they were triaging.
  const onMarkReadClick = (e: MouseEvent) => {
    e.stopPropagation();
    onMarkRead(n.id);
  };

  return (
    <li
      className={cx('ntf-row', `ntf-${tone}`, unread && 'unread', !clickable && 'ntf-plain')}
      role={clickable ? 'button' : undefined}
      tabIndex={clickable ? 0 : undefined}
      onClick={() => onOpen(n)}
      onKeyDown={clickable ? onKeyOpen : undefined}
    >
      <span className='ntf-ico' aria-hidden>
        <Icon />
      </span>
      <div className='ntf-main'>
        {/* The dot lives inside the title's inline flow: as a flex sibling it wraps onto a line of its
            own the moment the title needs two lines (which it does on a phone). */}
        <span className='ntf-title'>
          {unread && <i className='ntf-dot' aria-hidden />}
          {n.title}
        </span>
        <p className='ntf-msg'>{n.message}</p>
      </div>
      <span className='ntf-tags'>
        {team && (
          <span className='chip ntf-team' title={t('notifications.teamHint')}>
            {t('notifications.team')}
          </span>
        )}
      </span>
      <span className='ntf-time' title={fmtDateTime(n.occurredAt)}>
        <RelativeTimeLabel iso={n.occurredAt} />
      </span>
      {/* The slot is always there, even with no button in it: it is what keeps the timestamps of read
          and unread rows in the same column. */}
      <span className='ntf-act-slot'>
        {unread && (
          <button
            type='button'
            className='ntf-act'
            aria-label={t('notifications.markRead')}
            title={t('notifications.markRead')}
            onClick={onMarkReadClick}
          >
            <Check />
          </button>
        )}
      </span>
    </li>
  );
}

/** "há 2 h" / "ontem" / the plain date once the relative form stops carrying information. */
function RelativeTimeLabel({ iso }: { iso: string }) {
  const { t } = useTranslation();
  const rel = relativeTime(iso);
  if (rel.key === 'absolute') return <>{fmtInstantDate(iso)}</>;
  return <>{t(`notifications.relative.${rel.key}`, { n: 'n' in rel ? rel.n : undefined })}</>;
}
