import { useTranslation } from 'react-i18next';
import { type RemittanceLineStatus } from '../../api';

export function RemittanceLineStatusBadge({ s }: { s: RemittanceLineStatus }) {
  const { t } = useTranslation();
  const cls: Record<RemittanceLineStatus, string> = {
    Pending: 'badge-pendente',
    Paid: 'badge-pago',
    Returned: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`remittances.lineStatus.${s}`)}</span>;
}
