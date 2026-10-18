import { useTranslation } from 'react-i18next';
import { type PayableStatus, type InstallmentStatus } from '../../api';
import { railBucket, type RailLine } from './payment-rail';

/**
 * `Rejected` tem dois significados conforme a origem do título: vindo de requisição é devolução
 * (reversível — a requisição corrigida reabre este mesmo título), avulso é rejeição definitiva.
 * `returned` distingue os dois; sem ele o badge assume o caso avulso.
 */
export function payableStatusKey(s: PayableStatus, returned?: boolean): string {
  return s === 'Rejected' && returned ? 'payables.status.RejectedReturned' : `payables.status.${s}`;
}

export function PayableStatusBadge({ s, returned }: { s: PayableStatus; returned?: boolean }) {
  const { t } = useTranslation();
  const cls: Record<PayableStatus, string> = {
    Received: 'badge-pendente',
    PendingClassification: 'badge-pendente',
    PendingApproval: 'badge-pendente',
    PendingAccounting: 'badge-pendente',
    PendingTaxAnalysis: 'badge-pendente',
    Scheduled: 'badge-aprovado',
    Paid: 'badge-pago',
    // Devolvida ainda pode voltar ao fluxo — não é o vermelho de um desfecho terminal.
    Rejected: returned ? 'badge-pendente' : 'badge-rejeitado',
    Cancelled: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(payableStatusKey(s, returned))}</span>;
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
