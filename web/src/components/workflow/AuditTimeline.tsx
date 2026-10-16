import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { Circle } from 'lucide-react';
import { fmtDateTime } from '../../datetime';
import { SkeletonRows } from '../../ui';
import { brl } from '../../lib';
import { UserAvatar } from '../../user-avatar';

export interface AuditEvent {
  type: string;
  actorName: string;
  /** Foto do perfil Google do ator. Ausente nos eventos do sistema e em quem nunca entrou pela Google. */
  actorPictureUrl?: string | null;
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
  const reasonOf = (ev: AuditEvent) =>
    typeof ev.metadata?.reason === 'string' && ev.metadata.reason.trim() ? ev.metadata.reason : null;
  // Requisition reference on the origin event (RequisitionApproved) — the requisition that generated this title.
  const requisitionRefOf = (ev: AuditEvent) =>
    typeof ev.metadata?.requisitionCode === 'string' && ev.metadata.requisitionCode.trim()
      ? ev.metadata.requisitionCode
      : null;
  /** The one-line "what exactly" under an event: which account, which remittance, which origin. */
  function detailOf(ev: { type: string; metadata?: Record<string, unknown> }) {
    // Metadata keys depend on the event type, so read every one defensively (same contract note as the API).
    const str = (k: string) => (typeof ev.metadata?.[k] === 'string' ? ev.metadata[k] : undefined);
    if (ev.type === 'Classified' && str('accountCode'))
      return t('workflow.timeline.detail.classified', { code: str('accountCode'), name: str('accountName') ?? '' });
    if (ev.type === 'RemittanceGenerated' && str('fileName'))
      return t('workflow.timeline.detail.remittance', { file: str('fileName'), bank: str('bankCode') ?? '' });
    if (ev.type === 'Approved' && str('auto') === 'limit') {
      const limit = Number(str('limit'));
      return Number.isFinite(limit) && limit > 0
        ? t('workflow.timeline.detail.autoApprovedLimit', { limit: brl(limit) })
        : t('workflow.timeline.detail.autoApproved');
    }
    if (ev.type === 'BankReconciled') {
      const date = str('statementDate');
      const shown = date ? new Date(`${date}T12:00:00`).toLocaleDateString('pt-BR') : '';
      return str('mode') === 'linked'
        ? t('workflow.timeline.detail.reconciledLinked', { date: shown })
        : t('workflow.timeline.detail.reconciledSettled', { date: shown });
    }
    if (ev.type === 'PaymentRegistered' && str('source'))
      return t(`workflow.timeline.detail.paymentSource.${str('source')}`, { defaultValue: '' });
    return null;
  }

  return (
    <ol className='audit-timeline'>
      {events.map((ev, idx) => (
        <li key={idx} className='audit-event'>
          <span className='audit-event-dot'>
            <Circle size={10} />
          </span>
          <div className='audit-event-body'>
            <div className='audit-event-desc'>{describe(ev.type)}</div>
            {/* The requisition that generated this title, shown on its origin event. */}
            {requisitionRefOf(ev) && (
              <div className='audit-event-reason'>
                {t('workflow.timeline.requisitionRef', { code: requisitionRefOf(ev) })}
              </div>
            )}
            {/* The reason (devolução/cancelamento/estorno) travels in the event metadata — surface it so the
                timeline is the complete record and the standalone notices can be dropped (AEL-935). */}
            {reasonOf(ev) && <div className='audit-event-reason'>{reasonOf(ev)}</div>}
            {detailOf(ev) && <div className='audit-event-reason'>{detailOf(ev)}</div>}
            <div className='audit-event-meta'>
              <span className='audit-event-actor'>
                <UserAvatar className='audit-event-avatar' name={ev.actorName} pictureUrl={ev.actorPictureUrl} />
                {t('workflow.timeline.by', { actor: ev.actorName })}
              </span>
              <span className='audit-event-time'>{fmtDateTime(ev.timestamp)}</span>
            </div>
          </div>
        </li>
      ))}
    </ol>
  );
}
