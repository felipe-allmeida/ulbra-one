import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ChevronRight, Download } from 'lucide-react';
import { type PaymentHistoryItem, type PaymentMethod } from '../../api';
import { useBankAccounts, usePaymentsHistory } from '../../queries';
import { brlCents, SYSTEM_ACTOR_ID } from '../../lib';
import { useOriginNav, usePageParam, useResetPageOnChange } from '../../nav';
import { fmtDate } from '../../datetime';
import { useToast } from '../../toast';
import { Pager, Select, Skeleton, ToolbarField } from '../../ui';
import { downloadPaymentProofById } from '../payables/download';
import { PAYMENT_METHODS } from '../schemas';

/**
 * Histórico de baixas — cross-payable settlement history grouped by month (mirrors the
 * Controladoria inbox HistoryTab). Reversed baixas stay hidden (server default) — the payable
 * detail still shows them per installment.
 */
export function PaymentsHistoryTab({ companyId, search }: { companyId: string; search: string }) {
  const { t } = useTranslation();
  const openPayable = useOriginNav();
  const toast = useToast();
  const [page, setPage] = usePageParam();
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [method, setMethod] = useState<PaymentMethod | ''>('');
  useResetPageOnChange(setPage, [companyId, search, from, to, method]);

  const list = usePaymentsHistory(
    {
      companyId,
      from: from || undefined,
      to: to || undefined,
      method: method || undefined,
      search: search || undefined,
      page,
    },
    { enabled: !!companyId },
  );
  const items = list.data?.items ?? [];
  const accounts = useBankAccounts(companyId).data ?? [];
  const accountAlias = (id: string) => accounts.find((a) => a.id === id)?.alias ?? '—';

  // Group by payment month (local tz, same for key and label), preserving the server order.
  const groups = new Map<string, PaymentHistoryItem[]>();
  for (const p of items) {
    const dt = new Date(`${p.paymentDate}T00:00:00`);
    const key = `${dt.getFullYear()}-${dt.getMonth()}`;
    const bucket = groups.get(key);
    if (bucket) bucket.push(p);
    else groups.set(key, [p]);
  }
  const monthLabel = (isoDate: string) => {
    const s = new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric' }).format(
      new Date(`${isoDate}T00:00:00`),
    );
    return s.charAt(0).toUpperCase() + s.slice(1);
  };

  const proof = (p: PaymentHistoryItem) =>
    void downloadPaymentProofById(p.payableId, p.paymentId).catch(() => toast.error(t('payments.downloadError')));

  return (
    <>
      <div className='pq-history-filters'>
        <ToolbarField label={t('treasury.history.from')}>
          <input
            type='date'
            className='input'
            style={{ width: 150 }}
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
        </ToolbarField>
        <ToolbarField label={t('treasury.history.to')}>
          <input
            type='date'
            className='input'
            style={{ width: 150 }}
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
        </ToolbarField>
        <ToolbarField label={t('treasury.history.method')}>
          <Select
            value={method}
            style={{ width: 170 }}
            onChange={(e) => setMethod(e.target.value as PaymentMethod | '')}
          >
            <option value=''>{t('treasury.history.methodAll')}</option>
            {PAYMENT_METHODS.map((m) => (
              <option key={m} value={m}>
                {t(`payments.methods.${m}`)}
              </option>
            ))}
          </Select>
        </ToolbarField>
      </div>

      {list.isLoading ? (
        <div className='rq-list'>
          {Array.from({ length: 5 }).map((_, i) => (
            <div className='rq-row' key={i}>
              <div className='rq-row-main' style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                <Skeleton width={180} />
                <Skeleton width={240} height={10} />
              </div>
              <div className='rq-row-right'>
                <Skeleton width={90} />
              </div>
            </div>
          ))}
        </div>
      ) : items.length === 0 ? (
        <div className='rq-empty'>{t('treasury.history.empty')}</div>
      ) : (
        <>
          {[...groups.entries()].map(([month, payments]) => (
            <section key={month} className='rq-section'>
              <div className='rq-seclabel'>
                {monthLabel(payments[0].paymentDate)}
                <span className='rq-pip'>{payments.length}</span>
              </div>
              <div className='rq-list'>
                {payments.map((p) => (
                  <button
                    key={p.paymentId}
                    type='button'
                    className='rq-row'
                    onClick={() => void openPayable(`/app/payables/${p.payableId}`)}
                  >
                    <div className='rq-row-main'>
                      <div className='rq-row-title'>{p.counterpartyName}</div>
                      <div className='rq-row-meta'>
                        {[
                          fmtDate(p.paymentDate),
                          t(`payments.methods.${p.method}`),
                          accountAlias(p.bankAccountId),
                          p.installmentCount > 1
                            ? t('treasury.history.installmentOf', {
                                seq: p.installmentSequence,
                                count: p.installmentCount,
                              })
                            : null,
                          p.documentNumber ? `#${p.documentNumber}` : null,
                        ]
                          .filter(Boolean)
                          .join(' · ')}
                      </div>
                    </div>
                    <div className='rq-row-right'>
                      <span className='rq-row-value num'>{brlCents(p.amount)}</span>
                      {p.isReversed && (
                        <span className='badge-status badge-rejeitado'>{t('treasury.history.reversedBadge')}</span>
                      )}
                      {p.createdById === SYSTEM_ACTOR_ID && (
                        <span className='badge-status badge-aprovado'>{t('treasury.history.autoBadge')}</span>
                      )}
                      {p.hasProof && (
                        <span
                          role='button'
                          tabIndex={0}
                          className='btn btn-ghost'
                          style={{ padding: '2px 8px', fontSize: 12 }}
                          onClick={(e) => {
                            e.stopPropagation();
                            proof(p);
                          }}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault();
                              e.stopPropagation();
                              proof(p);
                            }
                          }}
                        >
                          <Download size={13} style={{ marginRight: 4 }} />
                          {t('treasury.history.downloadProof')}
                        </span>
                      )}
                    </div>
                    <ChevronRight className='rq-row-chevron' size={16} />
                  </button>
                ))}
              </div>
            </section>
          ))}
          <Pager page={page} totalPages={list.data?.totalPages ?? 1} onPage={setPage} />
        </>
      )}
    </>
  );
}
