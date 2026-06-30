import { useTranslation } from 'react-i18next';
import { type ReceivableStatus, type ReceivableInstallmentStatus } from '../../api';

export function ReceivableStatusBadge({ s }: { s: ReceivableStatus }) {
  const { t } = useTranslation();
  const cls: Record<ReceivableStatus, string> = {
    AwaitingReceipt: 'badge-pendente',
    Received: 'badge-pago',
    Cancelled: 'badge-rejeitado',
    WrittenOff: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`receivables.status.${s}`)}</span>;
}

export function ReceivableInstallmentStatusBadge({ s }: { s: ReceivableInstallmentStatus }) {
  const { t } = useTranslation();
  const cls: Record<ReceivableInstallmentStatus, string> = {
    Open: 'badge-pendente',
    PartiallyReceived: 'badge-aprovado',
    Received: 'badge-pago',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`receivableInstallments.status.${s}`)}</span>;
}

export function OverdueBadge() {
  const { t } = useTranslation();
  return <span className='badge-status badge-rejeitado'>{t('receivableInstallments.overdue')}</span>;
}
