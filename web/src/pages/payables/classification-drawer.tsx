import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePayable, useLedgerAccounts, useClassifyPayable } from '../../queries';
import { Drawer, Button, Field, Combobox, type ComboboxItem } from '../../ui';

export function ClassificationDrawer({ payableId, onClose }: { payableId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const { data: p } = usePayable(payableId);
  // Only analytical, active accounts can receive a payable — the classify endpoint rejects synthetic
  // (parent) accounts with LedgerAccount.NotPostable, so don't offer them in the picker.
  const accounting = useLedgerAccounts({ kind: 'Analytical', activeOnly: true }).data ?? [];
  const classify = useClassifyPayable();

  const [accountingId, setAccountingId] = useState<string | null>(null);

  // Pre-fill from the AI suggestion once the payable loads.
  useEffect(() => {
    if (p) {
      setAccountingId(p.accountingAccountId ?? p.suggestedAccountingAccountId ?? null);
    }
  }, [p]);

  const toItems = (xs: { id: string; code: string; name: string }[]): ComboboxItem[] =>
    xs.map((a) => ({ id: a.id, label: a.name, sub: a.code }));

  return (
    <Drawer
      open
      onClose={onClose}
      title={t('payables.classify.title')}
      sub={p?.counterpartyName}
      footer={
        <Button
          variant='brand'
          disabled={classify.isPending || !accountingId}
          onClick={() =>
            classify.mutate({ id: payableId, body: { accountingAccountId: accountingId } }, { onSuccess: onClose })
          }
        >
          {t('payables.classify.confirm')}
        </Button>
      }
    >
      <Field label={t('payables.classify.accounting')}>
        <Combobox value={accountingId} onChange={setAccountingId} items={toItems(accounting)} />
      </Field>
      {p?.classificationRationale && (
        <Field label={t('payables.classify.rationale')}>
          <div className='help'>{p.classificationRationale}</div>
        </Field>
      )}
    </Drawer>
  );
}
