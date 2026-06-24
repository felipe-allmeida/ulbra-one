import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { type Receivable, type ReceivableInstallment, type PaymentMethod, type RegisterReceiptInput } from '../../api';
import { resolver, fieldError, receiptSchema, type ReceiptValues } from '../schemas';
import { useBankAccounts, useRegisterReceipt, useUploadReceiptProof } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { Button, Input, Select, Field } from '../../ui';

const RECEIPT_METHODS: PaymentMethod[] = ['Pix', 'Ted', 'Boleto', 'Cash', 'DirectDebit', 'Other'];

/* ============ Recebimento (register receipt) form ============ */
export function ReceiptForm({
  receivable,
  installment,
  onDone,
  onCancel,
}: {
  receivable: Receivable;
  installment: ReceivableInstallment;
  onDone: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const contas = useBankAccounts(receivable.companyId).data ?? [];
  const [file, setFile] = useState<File | null>(null);

  const remaining = Math.round((installment.amount - installment.receivedAmount) * 100) / 100;

  const { control, handleSubmit, formState } = useForm<ReceiptValues>({
    resolver: resolver(receiptSchema),
    mode: 'onChange',
    defaultValues: {
      receiptDate: new Date().toISOString().slice(0, 10),
      amount: String(remaining > 0 ? remaining : installment.amount),
      bankAccountId: '',
      method: 'Pix',
      interest: '',
      fine: '',
      discount: '',
      notes: '',
    },
  });

  const register = useRegisterReceipt();
  const uploadProof = useUploadReceiptProof();
  const saving = register.isPending || uploadProof.isPending;
  const erro = register.error
    ? getErrorMessage(register.error)
    : uploadProof.error
      ? getErrorMessage(uploadProof.error)
      : null;

  const onValid = (v: ReceiptValues) => {
    const body: RegisterReceiptInput = {
      receiptDate: v.receiptDate,
      amount: Number(v.amount),
      bankAccountId: v.bankAccountId,
      method: v.method,
      interest: v.interest.trim() === '' ? 0 : Number(v.interest),
      fine: v.fine.trim() === '' ? 0 : Number(v.fine),
      discount: v.discount.trim() === '' ? 0 : Number(v.discount),
      notes: v.notes.trim() || null,
    };
    register.mutate(
      { receivableId: receivable.id, installmentId: installment.id, body },
      {
        onSuccess: (updated) => {
          if (file) {
            const inst = updated.installments.find((i) => i.id === installment.id);
            const newReceipt = inst?.receipts[inst.receipts.length - 1];
            if (newReceipt) {
              uploadProof.mutate(
                { receivableId: receivable.id, receiptId: newReceipt.id, file },
                { onSuccess: () => onDone() },
              );
              return;
            }
          }
          onDone();
        },
      },
    );
  };

  return (
    <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <div className='edit-grid'>
        <Field label={t('receipts.fields.receiptDate')}>
          <Controller control={control} name='receiptDate' render={({ field }) => <Input type='date' {...field} />} />
          {fieldError(formState.errors.receiptDate?.message) && (
            <div className='field-err'>{fieldError(formState.errors.receiptDate?.message)}</div>
          )}
        </Field>
        <Field label={t('receipts.fields.amount')}>
          <Controller
            control={control}
            name='amount'
            render={({ field }) => <Input {...field} inputMode='numeric' />}
          />
          {fieldError(formState.errors.amount?.message) && (
            <div className='field-err'>{fieldError(formState.errors.amount?.message)}</div>
          )}
        </Field>
        <Field label={t('receipts.fields.bankAccount')}>
          <Controller
            control={control}
            name='bankAccountId'
            render={({ field }) => (
              <Select {...field}>
                <option value=''>{t('receipts.fields.bankAccountSelect')}</option>
                {contas.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.alias}
                  </option>
                ))}
              </Select>
            )}
          />
          {fieldError(formState.errors.bankAccountId?.message) && (
            <div className='field-err'>{fieldError(formState.errors.bankAccountId?.message)}</div>
          )}
        </Field>
        <Field label={t('receipts.fields.method')}>
          <Controller
            control={control}
            name='method'
            render={({ field }) => (
              <Select {...field}>
                {RECEIPT_METHODS.map((m) => (
                  <option key={m} value={m}>
                    {t(`receipts.methods.${m}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>
        <Field label={t('receipts.fields.interest')}>
          <Controller
            control={control}
            name='interest'
            render={({ field }) => <Input {...field} inputMode='numeric' />}
          />
        </Field>
        <Field label={t('receipts.fields.fine')}>
          <Controller control={control} name='fine' render={({ field }) => <Input {...field} inputMode='numeric' />} />
        </Field>
        <Field label={t('receipts.fields.discount')}>
          <Controller
            control={control}
            name='discount'
            render={({ field }) => <Input {...field} inputMode='numeric' />}
          />
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Field label={t('receipts.fields.proof')} help={t('receipts.fields.proofHelp')}>
          <Input
            type='file'
            accept='application/pdf,image/png,image/jpeg'
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Field label={t('receipts.fields.notes')}>
          <Controller control={control} name='notes' render={({ field }) => <Input {...field} />} />
        </Field>
      </div>
      {erro && (
        <div className='create-err' style={{ marginTop: 10 }}>
          {erro}
        </div>
      )}
      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        <Button type='submit' variant='brand' disabled={!formState.isValid || saving}>
          {saving ? t('receipts.actions.registering') : t('receipts.actions.register')}
        </Button>
        <Button type='button' onClick={onCancel}>
          {t('forms.actions.cancel')}
        </Button>
      </div>
    </form>
  );
}
