import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Undo2 } from 'lucide-react';
import { type Receivable, type ReceivableInstallment, type Receipt } from '../../api';
import { useReverseReceipt } from '../../queries';
import { brl } from '../../lib';
import { Button, Input, ConfirmDialog } from '../../ui';
import { fmtDate } from '../../datetime';
import { fileErrorKey } from '../../download';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { downloadReceiptProof } from './download';

/* ============ Per-installment receipt history + actions ============ */
export function ReceiptHistory({
  receivable,
  installment,
}: {
  receivable: Receivable;
  installment: ReceivableInstallment;
}) {
  const { t } = useTranslation();
  const reverse = useReverseReceipt();
  const [toReverse, setToReverse] = useState<Receipt | null>(null);
  const [reverseReason, setReverseReason] = useState('');
  const [downloadErr, setDownloadErr] = useState<string | null>(null);

  const receipts = [...installment.receipts].sort((a, b) => a.receiptDate.localeCompare(b.receiptDate));
  if (receipts.length === 0) return null;

  // Mesma regra de papel do payables/payment-history.tsx: data é `title`, valor o único `amount`, forma é
  // `meta`. As ações foram testadas primeiro em `status` (visível sem expandir) mas o cluster de badge+2
  // botões não cabe na largura de `.rt-main` a 390px — sobrando por baixo do `.panel { overflow: hidden }`
  // (achado ao inspecionar a 390px: botão cortado, inalcançável). `detail` dá a largura cheia do card.
  const columns: Column<Receipt>[] = [
    {
      key: 'date',
      header: t('receipts.columns.date'),
      role: 'title',
      cell: (rcpt) => <span className='t-mono'>{fmtDate(rcpt.receiptDate)}</span>,
    },
    {
      key: 'amount',
      header: t('receipts.columns.amount'),
      role: 'amount',
      align: 'right',
      cell: (rcpt) => <span className='amount-cell'>{brl(rcpt.amount)}</span>,
    },
    {
      key: 'method',
      header: t('receipts.columns.method'),
      role: 'meta',
      cell: (rcpt) => t(`receipts.methods.${rcpt.method}`),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      cell: (rcpt) => (
        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center', flexWrap: 'wrap' }}>
          {rcpt.isReversed ? (
            <span className='badge-status badge-rejeitado'>{t('receipts.reversed')}</span>
          ) : (
            <>
              {rcpt.hasProof && (
                <Button
                  icon={<Download />}
                  onClick={() => {
                    setDownloadErr(null);
                    downloadReceiptProof(receivable.id, rcpt).catch((e) =>
                      setDownloadErr(t(fileErrorKey(e, 'receipts.downloadError'))),
                    );
                  }}
                >
                  {t('receipts.downloadProof')}
                </Button>
              )}
              <Button
                icon={<Undo2 />}
                onClick={() => {
                  setReverseReason('');
                  setToReverse(rcpt);
                }}
              >
                {t('receipts.reverse')}
              </Button>
            </>
          )}
        </div>
      ),
    },
  ];

  return (
    <>
      <ResponsiveTable rows={receipts} columns={columns} rowKey={(rcpt) => rcpt.id} />
      {downloadErr && <p className='create-err'>{downloadErr}</p>}

      {toReverse && (
        <ConfirmDialog
          title={t('receipts.reverseDialog.title')}
          danger
          busy={reverse.isPending}
          confirmLabel={t('receipts.reverseDialog.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('receipts.reverseDialog.message')}</div>
              <Input
                autoFocus
                value={reverseReason}
                placeholder={t('receipts.reverseDialog.placeholder')}
                onChange={(e) => setReverseReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            reverse.mutate(
              { receivableId: receivable.id, receiptId: toReverse.id, reason: reverseReason.trim() },
              { onSuccess: () => setToReverse(null) },
            )
          }
          onCancel={() => setToReverse(null)}
        />
      )}
    </>
  );
}
