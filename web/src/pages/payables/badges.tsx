import { useTranslation } from 'react-i18next';
import { type PayableStatus, type InstallmentStatus } from '../../api';
import { railBucket, type RailLine } from './payment-rail';

export function PayableStatusBadge({ s }: { s: PayableStatus }) {
  const { t } = useTranslation();
  const cls: Record<PayableStatus, string> = {
    Received: 'badge-pendente',
    PendingClassification: 'badge-pendente',
    PendingApproval: 'badge-pendente',
    PendingAccounting: 'badge-pendente',
    Scheduled: 'badge-aprovado',
    Paid: 'badge-pago',
    Rejected: 'badge-rejeitado',
    Cancelled: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`payables.status.${s}`)}</span>;
}

export function InstallmentStatusBadge({ s }: { s: InstallmentStatus }) {
  const { t } = useTranslation();
  const cls: Record<InstallmentStatus, string> = {
    Open: 'badge-pendente',
    PartiallyPaid: 'badge-aprovado',
    Paid: 'badge-pago',
    InRemittance: 'badge-remessa',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`installments.status.${s}`)}</span>;
}

export function InstallmentRailChip({ line, today }: { line: RailLine; today: string }) {
  const { t } = useTranslation();
  const bucket = railBucket(line, today);

  const clsMap: Record<typeof bucket, string> = {
    paid: 'badge-pago',
    inRemittance: 'badge-remessa',
    manual: 'badge-pendente',
    held: 'badge-pendente',
    nextSweep: 'badge-aprovado',
    scheduled: 'badge-aprovado',
  };

  const cls = clsMap[bucket];

  if (bucket === 'paid') {
    return <span className={`badge-status ${cls}`}>{t('installments.status.Paid')}</span>;
  }
  if (bucket === 'inRemittance') {
    return (
      <span className={`badge-status ${cls}`}>
        {t('installments.rail.inRemittance', { seq: line.remittanceSequence })}
      </span>
    );
  }
  if (bucket === 'manual') {
    const reason = line.manualReason ? t(`installments.manualReason.${line.manualReason}`) : undefined;
    return (
      <span className={`badge-status ${cls}`} title={reason}>
        {t('installments.rail.payManual')}
      </span>
    );
  }
  if (bucket === 'held') {
    return <span className={`badge-status ${cls}`}>{t('installments.rail.held')}</span>;
  }
  if (bucket === 'nextSweep') {
    return <span className={`badge-status ${cls}`}>{t('installments.rail.nextSweep')}</span>;
  }
  // scheduled
  return (
    <span className={`badge-status ${cls}`}>
      {t('installments.rail.scheduled')} {line.scheduledPaymentDate}
    </span>
  );
}
