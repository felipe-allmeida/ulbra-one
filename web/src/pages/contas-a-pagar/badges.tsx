import { useTranslation } from 'react-i18next';
import { type PayableStatus, type InstallmentStatus } from '../../api';

export function PayableStatusBadge({ s }: { s: PayableStatus }) {
  const { t } = useTranslation();
  const cls: Record<PayableStatus, string> = {
    PendingApproval: 'badge-pendente',
    Approved: 'badge-aprovado',
    Rejected: 'badge-rejeitado',
    Cancelled: 'badge-rejeitado',
    Settled: 'badge-pago',
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
