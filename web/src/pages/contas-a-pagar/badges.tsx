import { useTranslation } from 'react-i18next';
import { type PayableStatus, type InstallmentStatus, type RemittanceLineStatus } from '../../api';

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
    InRemittance: 'badge-remessa',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`installments.status.${s}`)}</span>;
}

export function RemittanceLineStatusBadge({ s }: { s: RemittanceLineStatus }) {
  const { t } = useTranslation();
  const cls: Record<RemittanceLineStatus, string> = {
    Pending: 'badge-pendente',
    Paid: 'badge-pago',
    Returned: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`remittances.lineStatus.${s}`)}</span>;
}
