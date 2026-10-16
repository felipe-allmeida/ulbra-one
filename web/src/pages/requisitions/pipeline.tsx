import { useTranslation } from 'react-i18next';
import { type RequisitionStatus } from '../../api';

/** Nós principais da esteira (Complemento é um desvio de Triagem/Aprovação; Cancelada é abortada). */
const NODES: { key: string; statuses: RequisitionStatus[] }[] = [
  { key: 'draft', statuses: ['Draft'] },
  { key: 'triage', statuses: ['Triage', 'Complement'] },
  { key: 'financialComplement', statuses: ['FinancialComplement'] },
  { key: 'approval', statuses: ['Approval'] },
  { key: 'inPayment', statuses: ['InPayment', 'AwaitingFiscalDocument'] },
  { key: 'done', statuses: ['Paid', 'Finalized'] },
];

/** Stepper horizontal que destaca o estágio atual (emula o pipeline do sistema legado). */
export function RequisitionPipeline({ status }: { status: RequisitionStatus }) {
  const { t } = useTranslation();
  const idx = NODES.findIndex((n) => n.statuses.includes(status));
  const cancelled = status === 'Cancelled';
  const complement = status === 'Complement';

  return (
    <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', margin: '4px 0 12px' }}>
      {NODES.map((n, i) => {
        const done = idx >= 0 && i < idx;
        const active = idx === i && !cancelled;
        const cls = active ? 'badge-aprovado' : done ? 'badge-pago' : 'badge-pendente';
        return (
          <div key={n.key} style={{ display: 'flex', alignItems: 'center' }}>
            <span className={`badge-status ${cls}`} style={{ opacity: !active && !done ? 0.5 : 1 }}>
              {i + 1}. {t(`requisitions.pipeline.${n.key}`)}
            </span>
            {i < NODES.length - 1 && (
              <span style={{ width: 18, height: 2, background: 'var(--border)', margin: '0 6px' }} />
            )}
          </div>
        );
      })}
      {complement && (
        <span className='badge-status badge-pendente' style={{ marginLeft: 8 }}>
          ↩ {t('requisitions.pipeline.complement')}
        </span>
      )}
      {cancelled && (
        <span className='badge-status badge-rejeitado' style={{ marginLeft: 8 }}>
          {t('requisitions.status.Cancelled')}
        </span>
      )}
    </div>
  );
}
