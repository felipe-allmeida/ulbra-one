import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { type Payable } from '../../api';
import { useCostCenters, useExpenseCategories, useLedgerAccounts, useRequisition } from '../../queries';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { fmtDate } from '../../datetime';
import { daysUntil, dueTone } from '../../components/workflow/sla';
import { PayableStatusBadge } from './badges';

/**
 * Card unificado de dados da conta a pagar (espelha o `RequisitionSummaryCard`): cabeçalho com título +
 * status/SLA + valor total, seguido das seções agrupadas Classificação e Financeiro em pares label→valor.
 * Substitui a antiga grade de `.info-card` soltos — reaproveita as classes genéricas `rq-*`.
 */
export function PayableSummaryCard({ payable: p }: { payable: Payable }) {
  const { t } = useTranslation();
  const openDetail = useOriginNav();
  const centros = useCostCenters(p.companyId).data ?? [];
  const categorias = useExpenseCategories(true).data ?? [];
  const accountingAccounts = useLedgerAccounts({}).data ?? [];
  // Requisição de origem (AEL-793): resolve o código amigável + status para o link de volta; a query fica
  // desabilitada quando o payable é manual (sem origem).
  const originReq = useRequisition(p.originRequisitionId ?? undefined).data;

  const centroNome = centros.find((c) => c.id === p.costCenterId)?.name ?? '—';
  const categoriaNome = categorias.find((c) => c.id === p.categoryId)?.name ?? '—';
  const accountLabel = (() => {
    if (!p.accountingAccountId) return t('payables.notClassified');
    const a = accountingAccounts.find((x) => x.id === p.accountingAccountId);
    return a ? `${a.code} — ${a.name}` : t('payables.notClassified');
  })();
  const showNet = p.status === 'Scheduled' || p.status === 'Paid';
  // The due tag only makes sense while the account is open; on terminal states (paid/rejected/cancelled) the
  // deadline is moot and "Vencido há Nd" is misleading.
  const isSettled = p.status === 'Paid' || p.status === 'Rejected' || p.status === 'Cancelled';

  return (
    <div className='rq-summary'>
      <div className='rq-summary-head'>
        <div className='rq-summary-heading'>
          <h2 className='rq-summary-title'>{p.description}</h2>
          <div className='rq-summary-meta'>
            <PayableStatusBadge s={p.status} />
            <span className={`badge-status ${p.isClassified ? 'badge-pago' : 'badge-pendente'}`}>
              {p.isClassified ? t('payables.classified') : t('payables.unclassified')}
            </span>
            {!isSettled && p.installments[0] && <DueChip dueDate={p.installments[0].dueDate} />}
          </div>
        </div>
        <div className='rq-summary-amount'>
          <div className='rq-summary-amount-value'>{brl(p.totalAmount)}</div>
          <div className='rq-summary-amount-label'>{t('payables.cards.totalAmount')}</div>
        </div>
      </div>

      <div className='rq-summary-sec'>
        <div className='rq-seclabel rq-seclabel-brand'>{t('payables.summary.classification')}</div>
        <div className='rq-kv'>
          <KvCell label={t('payables.cards.counterparty')} value={p.counterpartyName || '—'} />
          <KvCell label={t('payables.cards.costCenter')} value={centroNome} />
          <KvCell label={t('payables.cards.category')} value={categoriaNome} />
          <KvCell label={t('payables.cards.accountingAccount')} value={accountLabel} />
        </div>
      </div>

      <div className='rq-summary-sec'>
        <div className='rq-seclabel rq-seclabel-brand'>{t('payables.summary.financial')}</div>
        <div className='rq-kv'>
          {showNet && <KvCell label={t('payables.cards.netTotal')} value={brl(p.netTotal)} />}
          <KvCell label={t('payables.cards.documentNumber')} value={p.documentNumber || '—'} />
          <KvCell label={t('payables.cards.issueDate')} value={p.issueDate ? fmtDate(p.issueDate) : '—'} />
          {p.originRequisitionId && (
            <KvCell
              label={t('payables.cards.originRequisition')}
              value={
                <a
                  href={`/app/requisitions/${p.originRequisitionId}`}
                  onClick={(e) => {
                    e.preventDefault();
                    openDetail(`/app/requisitions/${p.originRequisitionId}`);
                  }}
                  style={{ color: 'var(--accent, #2563eb)', cursor: 'pointer', textDecoration: 'none' }}
                >
                  {originReq ? `#${originReq.code}` : t('payables.cards.viewRequisition')}
                  {originReq && ` · ${t('requisitions.status.' + originReq.status)}`}
                </a>
              }
            />
          )}
        </div>
      </div>
    </div>
  );
}

/** Chip de prazo do 1º vencimento (só o vencimento — o status já vem no PayableStatusBadge). */
function DueChip({ dueDate }: { dueDate: string }) {
  const { t } = useTranslation();
  const days = daysUntil(dueDate);
  const tone = dueTone(days);
  const text =
    days === 0
      ? t('workflow.sla.today')
      : days < 0
        ? t('workflow.sla.overdue', { days: -days })
        : t('workflow.sla.dueIn', { days });
  return <span className={`sla-chip sla-${tone}`}>{text}</span>;
}

/** Célula label→valor (label muted em cima, valor em negrito embaixo) do grid de 2 colunas. */
function KvCell({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className='rq-kv-cell'>
      <div className='info-label'>{label}</div>
      <div className='rq-kv-value'>{value}</div>
    </div>
  );
}
