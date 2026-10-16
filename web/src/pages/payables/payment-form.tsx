import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { type Payable, type Installment, type RegisterPaymentInput } from '../../api';
import { resolver, fieldError, paymentSchema, PAYMENT_METHODS, type PaymentValues } from '../schemas';
import { useBankAccounts, useRegisterPayment, useUploadPaymentProof, useVendor } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { moneyInput, parseMoneyInput } from '../../lib';
import { Button, CurrencyInput, Input, Select, Field } from '../../ui';

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

  // Only the payment methods the vendor accepts (empty/loading = all — AEL-712).
  const vendor = useVendor(payable.vendorId).data;
  const accepted = vendor?.acceptedPaymentMethods ?? [];
  const allowedMethods = accepted.length > 0 ? PAYMENT_METHODS.filter((m) => accepted.includes(m)) : PAYMENT_METHODS;

  // Outstanding balance is NET of withholdings — the server rejects anything above it
  // (Payment.Overpay); suggesting the gross here used to make every prefilled payment fail.
  const remaining = Math.round((installment.netAmount - installment.paidAmount) * 100) / 100;

  const { control, handleSubmit, formState, setValue, watch } = useForm<PaymentValues>({
    resolver: resolver(paymentSchema),
    mode: 'onChange',
    defaultValues: {
      paymentDate: new Date().toISOString().slice(0, 10),
      amount: moneyInput(remaining > 0 ? remaining : installment.netAmount),
      bankAccountId: '',
      method: 'Pix',
      interest: '',
      fine: '',
      discount: '',
      notes: '',
    },
  });

  // If the selected method isn't among the vendor's accepted ones, fall back to the first allowed.
  const method = watch('method');
  useEffect(() => {
    if (allowedMethods.length > 0 && !allowedMethods.includes(method)) {
      setValue('method', allowedMethods[0], { shouldValidate: true });
    }
  }, [allowedMethods, method, setValue]);

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
      amount: parseMoneyInput(v.amount) ?? 0,
      bankAccountId: v.bankAccountId,
      method: v.method,
      interest: parseMoneyInput(v.interest) ?? 0,
      fine: parseMoneyInput(v.fine) ?? 0,
      discount: parseMoneyInput(v.discount) ?? 0,
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
            render={({ field }) => (
              <CurrencyInput
                value={field.value}
                onValueChange={field.onChange}
                onBlur={field.onBlur}
                name={field.name}
              />
            )}
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
                {allowedMethods.map((m) => (
                  <option key={m} value={m}>
                    {t(`payments.methods.${m}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>
        {method === 'Pix' && (
          <Field label={t('registry.vendor.pixKey')}>
            <Input value={vendor?.bankInfo?.pixKey || ''} readOnly placeholder={t('payments.fields.noPixKey')} />
          </Field>
        )}
        <Field label={t('payments.fields.interest')}>
          <Controller
            control={control}
            name='interest'
            render={({ field }) => (
              <CurrencyInput
                value={field.value}
                onValueChange={field.onChange}
                onBlur={field.onBlur}
                name={field.name}
              />
            )}
          />
        </Field>
        <Field label={t('payments.fields.fine')}>
          <Controller
            control={control}
            name='fine'
            render={({ field }) => (
              <CurrencyInput
                value={field.value}
                onValueChange={field.onChange}
                onBlur={field.onBlur}
                name={field.name}
              />
            )}
          />
        </Field>
        <Field label={t('payments.fields.discount')}>
          <Controller
            control={control}
            name='discount'
            render={({ field }) => (
              <CurrencyInput
                value={field.value}
                onValueChange={field.onChange}
                onBlur={field.onBlur}
                name={field.name}
              />
            )}
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
