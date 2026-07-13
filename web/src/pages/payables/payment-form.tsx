import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { type Payable, type Installment, type PaymentMethod, type RegisterPaymentInput } from '../../api';
import { resolver, fieldError, paymentSchema, type PaymentValues } from '../schemas';
import { useBankAccounts, useRegisterPayment, useUploadPaymentProof } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { Button, Input, Select, Field } from '../../ui';

const PAYMENT_METHODS: PaymentMethod[] = ['Pix', 'Ted', 'Boleto', 'Cash', 'DirectDebit', 'Other'];

/* ============ Baixa (register payment) form ============ */
export function PaymentForm({
  payable,
  installment,
  onDone,
  onCancel,
}: {
  payable: Payable;
  installment: Installment;
  onDone: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const contas = useBankAccounts(payable.companyId).data ?? [];
  const [file, setFile] = useState<File | null>(null);

  // Outstanding balance is NET of withholdings — the server rejects anything above it
  // (Payment.Overpay); suggesting the gross here used to make every prefilled payment fail.
  const remaining = Math.round((installment.netAmount - installment.paidAmount) * 100) / 100;

  const { control, handleSubmit, formState } = useForm<PaymentValues>({
    resolver: resolver(paymentSchema),
    mode: 'onChange',
    defaultValues: {
      paymentDate: new Date().toISOString().slice(0, 10),
      amount: String(remaining > 0 ? remaining : installment.netAmount),
      bankAccountId: '',
      method: 'Pix',
      interest: '',
      fine: '',
      discount: '',
      notes: '',
    },
  });

  const register = useRegisterPayment();
  const uploadProof = useUploadPaymentProof();
  const saving = register.isPending || uploadProof.isPending;
  const erro = register.error
    ? getErrorMessage(register.error)
    : uploadProof.error
      ? getErrorMessage(uploadProof.error)
      : null;

  const onValid = (v: PaymentValues) => {
    const body: RegisterPaymentInput = {
      paymentDate: v.paymentDate,
      amount: Number(v.amount),
      bankAccountId: v.bankAccountId,
      method: v.method,
      interest: v.interest.trim() === '' ? 0 : Number(v.interest),
      fine: v.fine.trim() === '' ? 0 : Number(v.fine),
      discount: v.discount.trim() === '' ? 0 : Number(v.discount),
      notes: v.notes.trim() || null,
    };
    register.mutate(
      { payableId: payable.id, installmentId: installment.id, body },
      {
        onSuccess: (updated) => {
          if (file) {
            const inst = updated.installments.find((i) => i.id === installment.id);
            const newPayment = inst?.payments[inst.payments.length - 1];
            if (newPayment) {
              uploadProof.mutate(
                { payableId: payable.id, paymentId: newPayment.id, file },
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
        <Field label={t('payments.fields.paymentDate')}>
          <Controller control={control} name='paymentDate' render={({ field }) => <Input type='date' {...field} />} />
          {fieldError(formState.errors.paymentDate?.message) && (
            <div className='field-err'>{fieldError(formState.errors.paymentDate?.message)}</div>
          )}
        </Field>
        <Field label={t('payments.fields.amount')}>
          <Controller
            control={control}
            name='amount'
            render={({ field }) => <Input {...field} inputMode='numeric' />}
          />
          {fieldError(formState.errors.amount?.message) && (
            <div className='field-err'>{fieldError(formState.errors.amount?.message)}</div>
          )}
        </Field>
        <Field label={t('payments.fields.bankAccount')}>
          <Controller
            control={control}
            name='bankAccountId'
            render={({ field }) => (
              <Select {...field}>
                <option value=''>{t('payments.fields.bankAccountSelect')}</option>
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
        <Field label={t('payments.fields.method')}>
          <Controller
            control={control}
            name='method'
            render={({ field }) => (
              <Select {...field}>
                {PAYMENT_METHODS.map((m) => (
                  <option key={m} value={m}>
                    {t(`payments.methods.${m}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>
        <Field label={t('payments.fields.interest')}>
          <Controller
            control={control}
            name='interest'
            render={({ field }) => <Input {...field} inputMode='numeric' />}
          />
        </Field>
        <Field label={t('payments.fields.fine')}>
          <Controller control={control} name='fine' render={({ field }) => <Input {...field} inputMode='numeric' />} />
        </Field>
        <Field label={t('payments.fields.discount')}>
          <Controller
            control={control}
            name='discount'
            render={({ field }) => <Input {...field} inputMode='numeric' />}
          />
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Field label={t('payments.fields.proof')} help={t('payments.fields.proofHelp')}>
          <Input
            type='file'
            accept='application/pdf,image/png,image/jpeg'
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Field label={t('payments.fields.notes')}>
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
          {saving ? t('payments.actions.registering') : t('payments.actions.register')}
        </Button>
        <Button type='button' onClick={onCancel}>
          {t('forms.actions.cancel')}
        </Button>
      </div>
    </form>
  );
}
