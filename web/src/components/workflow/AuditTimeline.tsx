import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { Circle } from 'lucide-react';
import { fmtDateTime } from '../../datetime';
import { SkeletonRows } from '../../ui';

export interface AuditEvent {
  type: string;
  actorName: string;
  timestamp: string;
  metadata?: Record<string, unknown>;
}

/** Vertical timeline of typed audit events. Pure renderer — data fetched by the consumer. */
export function AuditTimeline({ events, loading }: { events: AuditEvent[]; loading?: boolean }) {
  const { t } = useTranslation();
  if (loading) {
    return (
      <table className='audit-timeline-skeleton'>
        <tbody>
          <SkeletonRows rows={3} cols={2} />
        </tbody>
      </table>
    );
  }
  if (events.length === 0) {
    return <div className='audit-timeline-empty'>{t('workflow.timeline.empty')}</div>;
  }
  const describe = (type: string) => {
    const key = `workflow.timeline.${type}`;
    return i18n.exists(key) ? t(key) : type;
  };
  return (
    <ol className='audit-timeline'>
      {events.map((ev, idx) => (
        <li key={idx} className='audit-event'>
          <span className='audit-event-dot'>
            <Circle size={10} />
          </span>
          <div className='audit-event-body'>
            <div className='audit-event-desc'>{describe(ev.type)}</div>
            <div className='audit-event-meta'>
              <span className='audit-event-actor'>{t('workflow.timeline.by', { actor: ev.actorName })}</span>
              <span className='audit-event-time'>{fmtDateTime(ev.timestamp)}</span>
            </div>
          </div>
        </li>
      ))}
    </ol>
  );
}
