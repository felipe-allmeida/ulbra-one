import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Undo2 } from 'lucide-react';
import { type Payable, type Installment, type Payment } from '../../api';
import { useReversePayment } from '../../queries';
import { brl, SYSTEM_ACTOR_ID } from '../../lib';
import { Button, Input, ConfirmDialog } from '../../ui';
import { fmtDate } from '../../datetime';
import { fileErrorKey } from '../../download';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { downloadPaymentProof } from './download';

/* ============ Per-installment payment history + actions ============ */
export function PaymentHistory({ payable, installment }: { payable: Payable; installment: Installment }) {
  const { t } = useTranslation();
  const reverse = useReversePayment();
  const [toReverse, setToReverse] = useState<Payment | null>(null);
  const [reverseReason, setReverseReason] = useState('');
  const [downloadErr, setDownloadErr] = useState<string | null>(null);

  const payments = [...installment.payments].sort((a, b) => a.paymentDate.localeCompare(b.paymentDate));
  if (payments.length === 0) return null;

  // Data identifica o lançamento (`title`); valor é o único `amount`; forma é `meta`. As tags/ações foram
  // testadas primeiro em `status` (visível sem expandir) mas o cluster de badge+2 botões não cabe na largura
  // de `.rt-main` a 390px — sobrando por baixo do `.panel { overflow: hidden }` (achado ao inspecionar a
  // 390px: botão cortado, inalcançável, sem barra de rolagem). `detail` dá a largura cheia do card quando
  // expandido; ver ResponsiveTable.tsx.
  const columns: Column<Payment>[] = [
    {
      key: 'date',
      header: t('payments.columns.date'),
      role: 'title',
      cell: (pay) => <span className='t-mono'>{fmtDate(pay.paymentDate)}</span>,
    },
    {
      key: 'amount',
      header: t('payments.columns.amount'),
      role: 'amount',
      align: 'right',
      cell: (pay) => <span className='amount-cell'>{brl(pay.amount)}</span>,
    },
    {
      key: 'method',
      header: t('payments.columns.method'),
      role: 'meta',
      cell: (pay) => t(`payments.methods.${pay.method}`),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      cell: (pay) => (
        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center', flexWrap: 'wrap' }}>
          {pay.createdById === SYSTEM_ACTOR_ID && (
            <span className='badge-status badge-aprovado'>{t('payments.autoPayTag')}</span>
          )}
          {pay.isReversed ? (
            <span className='badge-status badge-rejeitado'>{t('payments.reversed')}</span>
          ) : (
            <>
              {pay.hasProof && (
                <Button
                  icon={<Download />}
                  onClick={() => {
                    setDownloadErr(null);
                    downloadPaymentProof(payable.id, pay).catch((e) =>
                      setDownloadErr(t(fileErrorKey(e, 'payments.downloadError'))),
                    );
                  }}
                >
                  {t('payments.downloadProof')}
                </Button>
              )}
              <Button
                icon={<Undo2 />}
                onClick={() => {
                  setReverseReason('');
                  setToReverse(pay);
                }}
              >
                {t('payments.reverse')}
              </Button>
            </>
          )}
        </div>
      ),
    },
  ];

  return (
    <>
      <ResponsiveTable rows={payments} columns={columns} rowKey={(pay) => pay.id} />
      {downloadErr && <p className='create-err'>{downloadErr}</p>}

      {toReverse && (
        <ConfirmDialog
          title={t('payments.reverseDialog.title')}
          danger
          busy={reverse.isPending}
          confirmLabel={t('payments.reverseDialog.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payments.reverseDialog.message')}</div>
              <Input
                autoFocus
                value={reverseReason}
                placeholder={t('payments.reverseDialog.placeholder')}
                onChange={(e) => setReverseReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            reverse.mutate(
              { payableId: payable.id, paymentId: toReverse.id, reason: reverseReason.trim() },
              { onSuccess: () => setToReverse(null) },
            )
          }
          onCancel={() => setToReverse(null)}
        />
      )}
    </>
  );
}
