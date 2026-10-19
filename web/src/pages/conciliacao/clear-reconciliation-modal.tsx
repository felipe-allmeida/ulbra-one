import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useClearReconciliation } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Button, Field, Modal, Select } from '../../ui';

/** Bulk "clear/ignore" dialog: pick an account + a day, mark all still-open lines of that day as Ignored. */
export function ClearReconciliationModal({
  companyId,
  accounts,
  defaultBankAccountId,
  defaultDate,
  onClose,
}: {
  companyId: string;
  accounts: { id: string; alias: string }[];
  defaultBankAccountId: string;
  defaultDate: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const [bankAccountId, setBankAccountId] = useState(defaultBankAccountId);
  const [date, setDate] = useState(defaultDate);

  const clear = useClearReconciliation({
    onSuccess: (r) => {
      toast.success(t('conciliacao.clear.done', { count: r.ignored }));
      onClose();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const canSubmit = !!bankAccountId && !!date && !clear.isPending;

  return (
    <Modal
      title={t('conciliacao.clear.title')}
      sub={t('conciliacao.clear.sub')}
      onClose={onClose}
      footer={
        <>
          <Button
            variant='brand'
            disabled={!canSubmit}
            onClick={() => clear.mutate({ companyId, bankAccountId, date })}
          >
            {clear.isPending ? t('conciliacao.clear.clearing') : t('conciliacao.clear.confirm')}
          </Button>
          <Button onClick={onClose}>{t('forms.actions.cancel')}</Button>
        </>
      }
    >
      <Field label={t('conciliacao.bankAccount')}>
        <Select value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)}>
          <option value=''>{t('conciliacao.bankAccountSelect')}</option>
          {accounts.map((a) => (
            <option key={a.id} value={a.id}>
              {a.alias}
            </option>
          ))}
        </Select>
      </Field>
      <Field label={t('conciliacao.clear.day')}>
        <input type='date' className='input' value={date} onChange={(e) => setDate(e.target.value)} />
      </Field>
    </Modal>
  );
}
