import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Bell } from 'lucide-react';
import { useNotifications } from '../queries';
import { EmptyState, PageHeader, Panel, Pager } from '../ui';

/** Formats a DateTimeOffset string as a pt-BR locale date-time. */
function fmtDateTime(iso: string) {
  return new Date(iso).toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function NotificacoesPage() {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const { data, isLoading } = useNotifications(page);
  const items = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;

  return (
    <div>
      <PageHeader title={t('notifications.title')} />
      <Panel title={t('notifications.title')}>
        {isLoading ? (
          <p className='text-sm text-gray-500'>…</p>
        ) : items.length === 0 ? (
          <EmptyState icon={<Bell />} title={t('notifications.empty')} sub={t('notifications.emptyDescription')} />
        ) : (
          <ul className='divide-y divide-gray-100'>
            {items.map((n) => (
              <li key={n.id} className='flex flex-col gap-0.5 px-1 py-3'>
                <span className='text-sm font-medium text-gray-800'>{n.title}</span>
                <span className='text-sm text-gray-600'>{n.message}</span>
                <span className='text-xs text-gray-400'>
                  {t('notifications.occurredAt')}: {fmtDateTime(n.occurredAt)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
      <Pager page={page} totalPages={totalPages} onPage={setPage} />
    </div>
  );
}
