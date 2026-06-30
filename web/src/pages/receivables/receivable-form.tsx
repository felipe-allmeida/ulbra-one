import { useMemo } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, HandCoins } from 'lucide-react';
import { type Receivable, type ReceivableInput } from '../../api';
import { resolver, fieldError, receivableSchema, previewInstallments, type ReceivableValues } from '../schemas';
import { useApp } from '../../store';
import { useCreateReceivable, useReceivable, useUpdateReceivable } from '../../queries';
import { brl } from '../../lib';
import { getErrorMessage } from '../../apiError';
import { Button, Input, Select, Field, EmptyState } from '../../ui';
import { fmtDate } from '../../datetime';
import { useNames } from './use-names';
import { CustomerPicker } from './customer-picker';

/* ============ CREATE / EDIT FORM ============ */
function ReceivableForm({ initial }: { initial?: Receivable }) {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { t } = useTranslation();
  const { centros } = useNames(empresaId);

  const editing = !!initial;
  const fromInitial = (): ReceivableValues => {
    if (!initial) {
      return {
        payerId: '',
        costCenterId: '',
        description: '',
        documentNumber: '',
        issueDate: '',
        mode: 'single',
        totalAmount: '',
        installmentCount: '2',
        firstDueDate: '',
        intervalMonths: '1',
      };
    }
    const parcelas = [...initial.installments].sort((a, b) => a.sequence - b.sequence);
    const parcelado = parcelas.length > 1;
    let interval = 1;
    if (parcelado && parcelas.length >= 2) {
      const d0 = new Date(parcelas[0].dueDate + 'T00:00:00');
      const d1 = new Date(parcelas[1].dueDate + 'T00:00:00');
      interval = Math.max(0, (d1.getFullYear() - d0.getFullYear()) * 12 + (d1.getMonth() - d0.getMonth()));
    }
    return {
      payerId: initial.payerId,
      costCenterId: initial.costCenterId,
      description: initial.description,
      documentNumber: initial.documentNumber ?? '',
      issueDate: initial.issueDate ?? '',
      mode: parcelado ? 'installments' : 'single',
      totalAmount: String(initial.totalAmount),
      installmentCount: String(Math.max(2, parcelas.length)),
      firstDueDate: parcelas[0]?.dueDate ?? '',
      intervalMonths: String(interval || 1),
    };
  };

  const { control, handleSubmit, watch, formState } = useForm<ReceivableValues>({
    resolver: resolver(receivableSchema),
    mode: 'onChange',
    defaultValues: fromInitial(),
  });
  const mode = watch('mode');
  const totalAmount = watch('totalAmount');
  const installmentCount = watch('installmentCount');
  const firstDueDate = watch('firstDueDate');
  const intervalMonths = watch('intervalMonths');

  const preview = useMemo(() => {
    if (mode !== 'installments') return [];
    const total = Number(totalAmount);
    const count = Number(installmentCount);
    const interval = Number(intervalMonths);
    if (!(total > 0) || !Number.isInteger(count) || count < 2 || !firstDueDate) return [];
    return previewInstallments(total, count, firstDueDate, Number.isFinite(interval) ? interval : 1);
  }, [mode, totalAmount, installmentCount, firstDueDate, intervalMonths]);

  const create = useCreateReceivable();
  const update = useUpdateReceivable();
  const saving = create.isPending || update.isPending;
  const erro = create.error ? getErrorMessage(create.error) : update.error ? getErrorMessage(update.error) : null;

  const buildBody = (v: ReceivableValues): Omit<ReceivableInput, 'origin'> => {
    const base = {
      companyId: empresaId,
      payerId: v.payerId,
      costCenterId: v.costCenterId,
      description: v.description.trim(),
      documentNumber: v.documentNumber.trim() || null,
      issueDate: v.issueDate || null,
    };
    if (v.mode === 'single') {
      return { ...base, installments: [{ dueDate: v.firstDueDate, amount: Number(v.totalAmount) }] };
    }
    return {
      ...base,
      generate: {
        totalAmount: Number(v.totalAmount),
        count: Number(v.installmentCount),
        firstDueDate: v.firstDueDate,
        intervalMonths: Number(v.intervalMonths) || 1,
      },
    };
  };

  const onValid = (v: ReceivableValues) => {
    const body = buildBody(v);
    if (editing && initial) {
      update.mutate({ id: initial.id, body }, { onSuccess: (r) => nav(`/app/receivables/${r.id}`) });
    } else {
      create.mutate(body, { onSuccess: (r) => nav(`/app/receivables/${r.id}`) });
    }
  };

  const backTo = editing && initial ? `/app/receivables/${initial.id}` : '/app/receivables';

  return (
    <>
      <button className='backlink' onClick={() => nav(backTo)}>
        <ChevronLeft /> {t('common.back')}
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{editing ? t('receivables.editTitle') : t('receivables.newTitle')}</h1>
          <div className='detail-sub'>{empresa?.legalName ?? t('receivables.empty.noCompanyForm')}</div>
        </div>
      </div>

      {!empresa ? (
        <EmptyState
          icon={<HandCoins />}
          title={t('receivables.empty.noCompanyTitle')}
          sub={t('receivables.empty.noCompanyForm')}
        />
      ) : (
        <form className='card-form' onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
          <div className='edit-grid'>
            <Field label={t('receivables.fields.payer')}>
              <Controller
                control={control}
                name='payerId'
                render={({ field }) => (
                  <CustomerPicker
                    companyId={empresaId}
                    value={field.value || null}
                    onChange={(id) => field.onChange(id ?? '')}
                    invalid={!!formState.errors.payerId}
                  />
                )}
              />
              {fieldError(formState.errors.payerId?.message) && (
                <div className='field-err'>{fieldError(formState.errors.payerId?.message)}</div>
              )}
            </Field>
            <Field label={t('receivables.fields.costCenter')}>
              <Controller
                control={control}
                name='costCenterId'
                render={({ field }) => (
                  <Select {...field}>
                    <option value=''>{t('receivables.fields.costCenterSelect')}</option>
                    {centros.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.code} · {c.name}
                      </option>
                    ))}
                  </Select>
                )}
              />
              {fieldError(formState.errors.costCenterId?.message) && (
                <div className='field-err'>{fieldError(formState.errors.costCenterId?.message)}</div>
              )}
            </Field>
            <Field label={t('receivables.fields.documentNumber')}>
              <Controller
                control={control}
                name='documentNumber'
                render={({ field }) => <Input {...field} placeholder={t('receivables.placeholders.documentNumber')} />}
              />
            </Field>
            <Field label={t('receivables.fields.issueDate')}>
              <Controller control={control} name='issueDate' render={({ field }) => <Input type='date' {...field} />} />
            </Field>
          </div>

          <div style={{ marginTop: 14 }}>
            <Field label={t('receivables.fields.description')}>
              <Controller
                control={control}
                name='description'
                render={({ field }) => <Input {...field} placeholder={t('receivables.placeholders.description')} />}
              />
              {fieldError(formState.errors.description?.message) && (
                <div className='field-err'>{fieldError(formState.errors.description?.message)}</div>
              )}
            </Field>
          </div>

          <div style={{ marginTop: 14 }}>
            <Field label={t('receivables.fields.mode')}>
              <Controller
                control={control}
                name='mode'
                render={({ field }) => (
                  <div style={{ display: 'flex', gap: 8 }}>
                    <Button
                      type='button'
                      variant={field.value === 'single' ? 'brand' : 'default'}
                      onClick={() => field.onChange('single')}
                    >
                      {t('receivables.mode.single')}
                    </Button>
                    <Button
                      type='button'
                      variant={field.value === 'installments' ? 'brand' : 'default'}
                      onClick={() => field.onChange('installments')}
                    >
                      {t('receivables.mode.installments')}
                    </Button>
                  </div>
                )}
              />
            </Field>
          </div>

          <div className='edit-grid' style={{ marginTop: 14 }}>
            <Field label={mode === 'single' ? t('receivables.fields.amount') : t('receivables.fields.totalAmount')}>
              <Controller
                control={control}
                name='totalAmount'
                render={({ field }) => (
                  <Input {...field} inputMode='numeric' placeholder={t('receivables.placeholders.amount')} />
                )}
              />
              {fieldError(formState.errors.totalAmount?.message) && (
                <div className='field-err'>{fieldError(formState.errors.totalAmount?.message)}</div>
              )}
            </Field>
            <Field label={mode === 'single' ? t('receivables.fields.dueDate') : t('receivables.fields.firstDueDate')}>
              <Controller
                control={control}
                name='firstDueDate'
                render={({ field }) => <Input type='date' {...field} />}
              />
              {fieldError(formState.errors.firstDueDate?.message) && (
                <div className='field-err'>{fieldError(formState.errors.firstDueDate?.message)}</div>
              )}
            </Field>
            {mode === 'installments' && (
              <>
                <Field label={t('receivables.fields.installmentCount')}>
                  <Controller
                    control={control}
                    name='installmentCount'
                    render={({ field }) => <Input {...field} inputMode='numeric' />}
                  />
                  {fieldError(formState.errors.installmentCount?.message) && (
                    <div className='field-err'>{fieldError(formState.errors.installmentCount?.message)}</div>
                  )}
                </Field>
                <Field label={t('receivables.fields.intervalMonths')}>
                  <Controller
                    control={control}
                    name='intervalMonths'
                    render={({ field }) => <Input {...field} inputMode='numeric' />}
                  />
                </Field>
              </>
            )}
          </div>

          {mode === 'installments' && preview.length > 0 && (
            <div className='panel' style={{ marginTop: 14 }}>
              <div className='panel-head'>
                <span className='panel-title'>{t('receivables.preview.title')}</span>
              </div>
              <table>
                <thead>
                  <tr>
                    <th>{t('receivables.preview.installment')}</th>
                    <th>{t('receivables.preview.dueDate')}</th>
                    <th style={{ textAlign: 'right' }}>{t('receivables.preview.amount')}</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.map((p) => (
                    <tr key={p.sequence}>
                      <td className='t-mono'>
                        {p.sequence}/{preview.length}
                      </td>
                      <td className='t-mono'>{fmtDate(p.dueDate)}</td>
                      <td className='amount-cell' style={{ textAlign: 'right' }}>
                        {brl(p.amount)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {erro && (
            <div className='create-err' style={{ marginTop: 10 }}>
              {erro}
            </div>
          )}
          <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
            <Button type='submit' variant='brand' disabled={!formState.isValid || saving}>
              {saving
                ? t('receivables.actions.saving')
                : editing
                  ? t('receivables.actions.save')
                  : t('receivables.actions.create')}
            </Button>
            <Button type='button' onClick={() => nav(backTo)}>
              {t('receivables.actions.cancel')}
            </Button>
          </div>
        </form>
      )}
    </>
  );
}

export function NewReceivablePage() {
  return <ReceivableForm />;
}

export function EditReceivablePage() {
  const { id } = useParams();
  const query = useReceivable(id);
  const { t } = useTranslation();
  if (query.isLoading) return <div className='empty-state'>{t('receivables.loading')}</div>;
  if (!query.data)
    return (
      <EmptyState
        icon={<HandCoins />}
        title={t('receivables.empty.notFoundTitle')}
        sub={t('receivables.empty.notFoundSub')}
      />
    );
  return <ReceivableForm initial={query.data} />;
}
