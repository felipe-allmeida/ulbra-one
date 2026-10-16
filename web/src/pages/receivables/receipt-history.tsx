import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Undo2 } from 'lucide-react';
import { type Receivable, type ReceivableInstallment, type Receipt } from '../../api';
import { useReverseReceipt } from '../../queries';
import { brl } from '../../lib';
import { Button, Input, ConfirmDialog } from '../../ui';
import { fmtDate } from '../../datetime';
import { fileErrorKey } from '../../download';
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

  return (
    <>
      <table>
        <thead>
          <tr>
            <th>{t('receipts.columns.date')}</th>
            <th style={{ textAlign: 'right' }}>{t('receipts.columns.amount')}</th>
            <th>{t('receipts.columns.method')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {receipts.map((rcpt) => (
            <tr key={rcpt.id}>
              <td className='t-mono'>{fmtDate(rcpt.receiptDate)}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(rcpt.amount)}
              </td>
              <td>{t(`receipts.methods.${rcpt.method}`)}</td>
              <td className='td-action'>
                <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center' }}>
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
              </td>
            </tr>
          ))}
        </tbody>
      </table>
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
