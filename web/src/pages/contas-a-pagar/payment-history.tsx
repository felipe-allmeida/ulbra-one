import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Undo2 } from 'lucide-react';
import { type Payable, type Installment, type Payment } from '../../api';
import { useReversePayment } from '../../queries';
import { brl } from '../../lib';
import { Button, Input, ConfirmDialog } from '../../ui';
import { fmtData } from '../../datetime';
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

  return (
    <>
      <table>
        <thead>
          <tr>
            <th>{t('payments.columns.date')}</th>
            <th style={{ textAlign: 'right' }}>{t('payments.columns.amount')}</th>
            <th>{t('payments.columns.method')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {payments.map((pay) => (
            <tr key={pay.id}>
              <td className='t-mono'>{fmtData(pay.paymentDate)}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(pay.amount)}
              </td>
              <td>{t(`payments.methods.${pay.method}`)}</td>
              <td className='td-action'>
                <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center' }}>
                  {pay.isReversed ? (
                    <span className='badge-status badge-rejeitado'>{t('payments.reversed')}</span>
                  ) : (
                    <>
                      {pay.hasProof && (
                        <Button
                          icon={<Download />}
                          onClick={() => {
                            setDownloadErr(null);
                            downloadPaymentProof(payable.id, pay).catch(() =>
                              setDownloadErr(t('payments.downloadError')),
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
              </td>
            </tr>
          ))}
        </tbody>
      </table>
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
