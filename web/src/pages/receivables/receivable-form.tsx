import { useMemo } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, HandCoins } from 'lucide-react';
import { type Receivable, type ReceivableInput } from '../../api';
import { resolver, fieldError, receivableSchema, previewInstallments, type ReceivableValues } from '../schemas';
import { useApp } from '../../store';
import { useCreateReceivable, useReceivable, useUpdateReceivable } from '../../queries';
import { brl, moneyInput, parseMoneyInput } from '../../lib';
import { getErrorMessage } from '../../apiError';
import { Button, CurrencyInput, Input, Select, Field, EmptyState, LoadingState } from '../../ui';
import { fmtDate } from '../../datetime';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { useNames } from './use-names';
import { CustomerPicker } from './customer-picker';

/* ============ CREATE / EDIT FORM ============ */
function ReceivableForm({ initial }: { initial?: Receivable }) {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const location = useLocation();
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
      totalAmount: moneyInput(initial.totalAmount),
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
    const total = parseMoneyInput(totalAmount) ?? 0;
    const count = Number(installmentCount);
    const interval = Number(intervalMonths);
    if (!(total > 0) || !Number.isInteger(count) || count < 2 || !firstDueDate) return [];
    return previewInstallments(total, count, firstDueDate, Number.isFinite(interval) ? interval : 1);
  }, [mode, totalAmount, installmentCount, firstDueDate, intervalMonths]);

  // Mesma regra de papel do payable-form.tsx: parcela é a única `title`, vencimento `meta`, valor o único
  // `amount`.
  const previewColumns: Column<{ sequence: number; dueDate: string; amount: number }>[] = [
    {
      key: 'sequence',
      header: t('receivables.preview.installment'),
      role: 'title',
      cell: (p) => (
        <span className='t-mono'>
          {p.sequence}/{preview.length}
        </span>
      ),
    },
    {
      key: 'dueDate',
      header: t('receivables.preview.dueDate'),
      role: 'meta',
      cell: (p) => <span className='t-mono'>{fmtDate(p.dueDate)}</span>,
    },
    {
      key: 'amount',
      header: t('receivables.preview.amount'),
      role: 'amount',
      align: 'right',
      cell: (p) => <span className='amount-cell'>{brl(p.amount)}</span>,
    },
  ];

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
      return { ...base, installments: [{ dueDate: v.firstDueDate, amount: parseMoneyInput(v.totalAmount) ?? 0 }] };
    }
    return {
      ...base,
      generate: {
        totalAmount: parseMoneyInput(v.totalAmount) ?? 0,
        count: Number(v.installmentCount),
        firstDueDate: v.firstDueDate,
        intervalMonths: Number(v.intervalMonths) || 1,
      },
    };
  };

  const onValid = (v: ReceivableValues) => {
    const body = buildBody(v);
    if (editing && initial) {
      update.mutate(
        { id: initial.id, body },
        { onSuccess: (r) => nav(`/app/receivables/${r.id}`, { state: location.state }) },
      );
    } else {
      create.mutate(body, { onSuccess: (r) => nav(`/app/receivables/${r.id}`, { state: location.state }) });
    }
  };

  const backTo = editing && initial ? `/app/receivables/${initial.id}` : '/app/receivables';

  return (
    <>
      <button className='backlink' onClick={() => nav(backTo, { state: location.state })}>
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
                  <CurrencyInput
                    value={field.value}
                    onValueChange={field.onChange}
                    onBlur={field.onBlur}
                    name={field.name}
                    placeholder={t('receivables.placeholders.amount')}
                  />
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
              <ResponsiveTable rows={preview} columns={previewColumns} rowKey={(p) => String(p.sequence)} />
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
            <Button type='button' onClick={() => nav(backTo, { state: location.state })}>
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
  if (query.isLoading) return <LoadingState />;
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
