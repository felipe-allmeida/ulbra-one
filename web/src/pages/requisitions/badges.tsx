import { useTranslation } from 'react-i18next';
import { FileText } from 'lucide-react';
import { type Requisition, type RequisitionStatus } from '../../api';
import { stageSla } from './sla';

/** Cores de status (mesma paleta do Payables): pendente=âmbar, aprovado=azul, pago=verde, rejeitado=vermelho. */
const STATUS_CLS: Record<RequisitionStatus, string> = {
  Draft: 'badge-pendente',
  Triage: 'badge-pendente',
  FinancialComplement: 'badge-pendente',
  Approval: 'badge-pendente',
  Complement: 'badge-pendente',
  InPayment: 'badge-aprovado',
  AwaitingFiscalDocument: 'badge-pendente',
  Paid: 'badge-pago',
  Finalized: 'badge-pago',
  Cancelled: 'badge-rejeitado',
};

export function RequisitionStatusBadge({ s }: { s: RequisitionStatus }) {
  const { t } = useTranslation();
  return <span className={`badge-status ${STATUS_CLS[s]}`}>{t(`requisitions.status.${s}`)}</span>;
}

/** "Nota Fiscal pendente" tag — a post-approval requisition still missing its NF (requester needs to send it). */
export function FiscalPendingTag() {
  const { t } = useTranslation();
  return (
    <span
      className='badge-status badge-pendente'
      title={t('requisitions.fiscalPending.tagTitle')}
      style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}
    >
      <FileText size={12} style={{ flexShrink: 0 }} />
      {t('requisitions.fiscalPending.tag')}
    </span>
  );
}

/** SLA badge — shown ONLY once the 24-business-hour deadline is exceeded (Triage/Approval).
 * Within the deadline there is no chip (it would just be noise on every in-flight requisition). */
export function RequisitionSlaChip({ req, now }: { req: Requisition; now?: Date }) {
  const { t } = useTranslation();
  if (req.status !== 'Triage' && req.status !== 'Approval') return null;
  const sla = stageSla(req.statusChangedAt, 24, now);
  if (!sla?.overdue) return null;
  return <span className='sla-chip sla-danger'>{t('requisitions.sla.overdue', { hours: -sla.remaining })}</span>;
}
