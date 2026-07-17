import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { type BankTransaction } from '../../api';
import { useMatchTransaction, usePayables } from '../../queries';
import { brl } from '../../lib';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Button, Combobox, Drawer, Field } from '../../ui';
import { fmtDate } from '../../datetime';

/** Manual installment picker: match a bank transaction to an open installment of a Scheduled payable. */
export function ManualMatchDrawer({
  companyId,
  transaction,
  onClose,
}: {
  companyId: string;
  transaction: BankTransaction;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const [installmentId, setInstallmentId] = useState<string | null>(null);

  // Open installments come from the company's Scheduled payables, flattened.
  const payables = usePayables({ companyId, status: 'Scheduled', pageSize: 100 }, { enabled: !!companyId });
  const items = useMemo(() => {
    const list = payables.data?.items ?? [];
    return list
      .filter((p) => p.installmentStatus === 'Open' || p.installmentStatus === 'PartiallyPaid')
      .map((p) => ({
        id: p.installmentId,
        label: `${p.counterpartyName || '—'} · ${fmtDate(p.dueDate)}`,
        // Net of withholdings — the value that actually matches the bank debit.
        sub: `${p.sequence}/${p.installmentCount} · ${brl(p.netAmount)}`,
      }));
  }, [payables.data]);

  const match = useMatchTransaction({
    onSuccess: () => {
      toast.success(t('conciliacao.actions.matchManual'));
      onClose();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  return (
    <Drawer open title={t('conciliacao.matchDialog.title')} sub={t('conciliacao.matchDialog.sub')} onClose={onClose}>
      <Field label={t('conciliacao.matchDialog.installment')}>
        <Combobox
          value={installmentId}
          onChange={setInstallmentId}
          items={items}
          loading={payables.isLoading}
          placeholder={t('conciliacao.matchDialog.select')}
          searchPlaceholder={t('conciliacao.matchDialog.search')}
          emptyText={t('conciliacao.matchDialog.empty')}
        />
      </Field>
      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        <Button
          variant='brand'
          disabled={!installmentId || match.isPending}
          onClick={() => installmentId && match.mutate({ id: transaction.id, installmentId })}
        >
          {match.isPending ? t('conciliacao.matchDialog.matching') : t('conciliacao.matchDialog.confirm')}
        </Button>
        <Button onClick={onClose}>{t('forms.actions.cancel')}</Button>
      </div>
    </Drawer>
  );
}
