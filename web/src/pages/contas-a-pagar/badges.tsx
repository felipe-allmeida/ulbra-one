import { useTranslation } from 'react-i18next';
import { type PayableStatus, type InstallmentStatus } from '../../api';

export function PayableStatusBadge({ s }: { s: PayableStatus }) {
  const { t } = useTranslation();
  const cls: Record<PayableStatus, string> = {
    Received: 'badge-pendente',
    PendingClassification: 'badge-pendente',
    PendingApproval: 'badge-pendente',
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
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`installments.status.${s}`)}</span>;
}
