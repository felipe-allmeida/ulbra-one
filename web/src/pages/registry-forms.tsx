import { useState, type ReactNode } from 'react';
import { Sparkles } from 'lucide-react';
import { useForm, Controller, type Control, type UseFormSetValue } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import {
  api,
  PersonType,
  type Address,
  type Vendor,
  type Customer,
  type Employee,
  type Unit,
  type CostCenter,
} from '../api';
import { Button, Field, Input, MaskedInput, Select } from '../ui';
import { useToast } from '../toast';
import { onlyDigits, maskCpf, maskCnpj, maskCep, maskPhone } from '../format';
import { getErrorMessage, ApiError } from '../apiError';
import { useSaveCustomer, useSaveEmployee, useSaveVendor } from '../queries';
import {
  vendorSchema,
  customerSchema,
  employeeSchema,
  resolver,
  fieldError,
  TAX_REGIMES,
  SIMPLES_ANEXOS,
  PAYMENT_SCHEDULE_MODES,
  PAYMENT_METHODS,
  type VendorValues,
  type CustomerValues,
  type EmployeeValues,
  type AddressValues,
} from './schemas';

/* ---------- shared local state for an editable address ---------- */
const emptyAddress: AddressValues = {
  cep: '',
  street: '',
  number: '',
  complement: '',
  neighborhood: '',
  city: '',
  state: '',
};

function fromAddress(a: Address | null): AddressValues {
  return a ? { ...a, complement: a.complement ?? '' } : { ...emptyAddress };
}
/** Returns null if the address is entirely blank (so we don't persist empty objects). */
function toAddress(a: AddressValues): Address | null {
  const filled = a.cep || a.street || a.number || a.neighborhood || a.city || a.state;
  if (!filled) return null;
  // CEP goes unmasked to match the 8-digit column (mirrors how document is stripped on submit).
  return { ...a, cep: onlyDigits(a.cep), complement: a.complement.trim() ? a.complement.trim() : null };
}

/** Fills only the address fields that are currently blank from a lookup result — never clobbers
 *  data the user already typed (important in edit mode). Returns the merged address + filled keys. */
function mergeAddressEmpty(
  current: AddressValues,
  incoming: Partial<Address>,
): { address: AddressValues; filled: (keyof AddressValues)[] } {
  const out = { ...current };
  const filled: (keyof AddressValues)[] = [];
  (['cep', 'street', 'number', 'neighborhood', 'city', 'state'] as (keyof AddressValues)[]).forEach((k) => {
    const cur = (current[k] ?? '').toString();
    const inc = (incoming[k] ?? '').toString();
    if (!cur.trim() && inc) {
      out[k] = inc;
      filled.push(k);
    }
  });
  return { address: out, filled };
}

export function AutoHint() {
  const { t } = useTranslation();
  return (
    <span className='auto-hint'>
      <Sparkles /> {t('forms.autoHint')}
    </span>
  );
}

/** Maps server-side field errors (ApiError.errors) back into the form via setError.
 *  Field keys are matched case-insensitively against the form's known fields. */
function applyServerErrors<T extends Record<string, unknown>>(
  err: unknown,
  fields: readonly (keyof T)[],
  setError: (name: keyof T, error: { type: string; message: string }) => void,
): boolean {
  if (!(err instanceof ApiError) || !err.errors) return false;
  let applied = false;
  for (const [key, messages] of Object.entries(err.errors)) {
    const match = fields.find((f) => String(f).toLowerCase() === key.toLowerCase());
    if (match && messages.length > 0) {
      setError(match, { type: 'server', message: messages.join(' ') });
      applied = true;
    }
  }
  return applied;
}

/** Inline error line under a field. */
function ErrMsg({ message }: { message?: string }) {
  const text = fieldError(message);
  return text ? <div className='field-err'>{text}</div> : null;
}

/* ---------- Address sub-form with CEP auto-fill ---------- */
function AddressFields<T extends { address: AddressValues }>({
  control,
  setValue,
  getAddress,
}: {
  control: Control<T>;
  setValue: UseFormSetValue<T>;
  getAddress: () => AddressValues;
}) {
  const toast = useToast();
  const { t } = useTranslation();
  const [auto, setAuto] = useState<Set<keyof AddressValues>>(new Set());

  // `address` is a known key on T (constrained above); the generic plumbing needs a cast.
  const setAddr = (a: AddressValues) =>
    setValue('address' as never, a as never, { shouldValidate: true, shouldDirty: true });

  const onCepBlur = async () => {
    const current = getAddress();
    const cep = onlyDigits(current.cep);
    if (cep.length !== 8) return;
    try {
      const r = await api.lookupCep(cep);
      const { address: merged, filled } = mergeAddressEmpty(current, {
        street: r.street,
        neighborhood: r.neighborhood,
        city: r.city,
        state: r.state,
      });
      if (filled.length === 0) return; // nothing blank to fill — don't overwrite the user's data
      setAddr(merged);
      setAuto(new Set(filled));
      toast.success(t('forms.autoFromCep'));
    } catch {
      toast.error(t('forms.cepNotFound'));
    }
  };

  const cls = (k: keyof AddressValues) => (auto.has(k) ? 'input-auto' : undefined);
  const clearAuto = (k: keyof AddressValues) => {
    if (auto.has(k)) {
      const n = new Set(auto);
      n.delete(k);
      setAuto(n);
    }
  };

  // Controlled text input bound to a single address field.
  const addrField = (k: keyof AddressValues, extra?: { maxLength?: number; upper?: boolean }) => (
    <Controller
      control={control}
      name={'address' as never}
      render={({ field }) => {
        const value = field.value as AddressValues;
        return (
          <Input
            className={cls(k)}
            maxLength={extra?.maxLength}
            value={value[k] ?? ''}
            onChange={(e) => {
              clearAuto(k);
              const v = extra?.upper ? e.target.value.toUpperCase() : e.target.value;
              field.onChange({ ...value, [k]: v });
            }}
          />
        );
      }}
    />
  );

  return (
    <>
      <div className='form-section'>Endereço</div>
      <div className='form-grid'>
        <Field label='CEP'>
          <Controller
            control={control}
            name={'address' as never}
            render={({ field }) => {
              const value = field.value as AddressValues;
              return (
                <MaskedInput
                  value={value.cep}
                  mask={maskCep}
                  onValueChange={(v) => field.onChange({ ...value, cep: v })}
                  onBlur={() => void onCepBlur()}
                  placeholder='00000-000'
                  inputMode='numeric'
                />
              );
            }}
          />
        </Field>
        <Field label='Número'>{addrField('number')}</Field>
        <div className='span-2'>
          <Field label={<>Logradouro {auto.has('street') && <AutoHint />}</>}>{addrField('street')}</Field>
        </div>
        <Field label='Complemento'>{addrField('complement')}</Field>
        <Field label={<>Bairro {auto.has('neighborhood') && <AutoHint />}</>}>{addrField('neighborhood')}</Field>
        <Field label={<>Cidade {auto.has('city') && <AutoHint />}</>}>{addrField('city')}</Field>
        <Field label={<>UF {auto.has('state') && <AutoHint />}</>}>
          {addrField('state', { maxLength: 2, upper: true })}
        </Field>
      </div>
    </>
  );
}

/* ============================ VENDOR ============================ */
const VENDOR_FIELDS = [
  'personType',
  'name',
  'tradeName',
  'document',
  'email',
  'phone',
  'paymentTerms',
  'bankInfo',
  'taxRegime',
  'paymentScheduleMode',
  'paymentScheduleDay',
  'acceptedPaymentMethods',
] as const;

export function VendorForm({
  companyId,
  initial,
  prefill,
  notice,
  onSaved,
  onCancel,
}: {
  companyId: string;
  initial?: Vendor;
  /** Create-mode pre-fill (e.g. issuer data read from a fiscal document). Ignored when editing. */
  prefill?: Partial<VendorValues>;
  /** Optional banner shown at the top of the form (e.g. "confirm the data collected from the NF"). */
  notice?: ReactNode;
  onSaved: (saved: Vendor) => void;
  onCancel: () => void;
}) {
  const toast = useToast();
  const { t } = useTranslation();
  const [nameAuto, setNameAuto] = useState<Set<'name' | 'tradeName'>>(new Set());

  const form = useForm<VendorValues>({
    resolver: resolver(vendorSchema),
    mode: 'onChange',
    defaultValues: {
      personType: initial?.personType ?? prefill?.personType ?? PersonType.Company,
      name: initial?.name ?? prefill?.name ?? '',
      tradeName: initial?.tradeName ?? prefill?.tradeName ?? '',
      document: initial?.document ?? prefill?.document ?? '',
      email: initial?.email ?? prefill?.email ?? '',
      phone: initial?.phone ?? prefill?.phone ?? '',
      paymentTerms: initial?.paymentTerms ?? prefill?.paymentTerms ?? '',
      bankInfo: {
        bankCode: initial?.bankInfo?.bankCode ?? prefill?.bankInfo?.bankCode ?? '',
        branch: initial?.bankInfo?.branch ?? prefill?.bankInfo?.branch ?? '',
        account: initial?.bankInfo?.account ?? prefill?.bankInfo?.account ?? '',
        accountDigit: initial?.bankInfo?.accountDigit ?? prefill?.bankInfo?.accountDigit ?? '',
        pixKey: initial?.bankInfo?.pixKey ?? prefill?.bankInfo?.pixKey ?? '',
      },
      taxRegime: initial?.taxRegime ?? prefill?.taxRegime ?? 'Unknown',
      cprbForeseen: initial?.cprbForeseen ?? null,
      simplesAnexo: initial?.simplesAnexo ?? null,
      paymentScheduleMode: initial?.paymentScheduleMode ?? prefill?.paymentScheduleMode ?? 'OnDueDate',
      paymentScheduleDay: initial?.paymentScheduleDay ?? prefill?.paymentScheduleDay ?? null,
      acceptedPaymentMethods: initial?.acceptedPaymentMethods ?? prefill?.acceptedPaymentMethods ?? [],
      address: initial ? fromAddress(initial.address ?? null) : (prefill?.address ?? { ...emptyAddress }),
    },
  });
  const { control, handleSubmit, setValue, getValues, watch, setError, formState } = form;

  const mutation = useSaveVendor({
    onSuccess: (saved) => {
      toast.success(initial ? 'Fornecedor atualizado.' : 'Fornecedor criado.');
      onSaved(saved);
    },
    onError: (e) => {
      if (!applyServerErrors<VendorValues>(e, VENDOR_FIELDS, setError)) toast.error(getErrorMessage(e));
    },
  });
  const saving = mutation.isPending;
  const personType = watch('personType');
  const isPj = personType === PersonType.Company;
  const paymentScheduleMode = watch('paymentScheduleMode');
  const taxRegime = watch('taxRegime');

  const onCnpjBlur = async () => {
    const document = getValues('document');
    if (!isPj || onlyDigits(document).length !== 14) return;
    try {
      const r = await api.lookupCnpj(onlyDigits(document));
      const autoFilled = new Set<'name' | 'tradeName'>();
      if (!getValues('name').trim()) {
        setValue('name', r.razaoSocial, { shouldValidate: true });
        autoFilled.add('name');
      }
      if (!getValues('tradeName').trim() && r.nomeFantasia) {
        setValue('tradeName', r.nomeFantasia);
        autoFilled.add('tradeName');
      }
      if (r.address) {
        setValue('address', mergeAddressEmpty(getValues('address'), r.address).address, { shouldValidate: true });
      }
      if (autoFilled.size) setNameAuto(autoFilled);
      toast.success(t('forms.autoFromCnpj'));
    } catch {
      toast.error(t('forms.cnpjNotFound'));
    }
  };

  const onValid = (v: VendorValues) => {
    const docDigits = onlyDigits(v.document);
    const bi = v.bankInfo;
    const hasBankInfo =
      bi.bankCode.trim() || bi.branch.trim() || bi.account.trim() || bi.accountDigit.trim() || bi.pixKey.trim();
    mutation.mutate({
      id: initial?.id,
      body: {
        companyId,
        personType: v.personType,
        name: v.name.trim(),
        tradeName: v.tradeName.trim() || null,
        document: docDigits,
        email: v.email.trim() || null,
        phone: v.phone.trim() || null,
        address: toAddress(v.address),
        paymentTerms: v.paymentTerms.trim() || null,
        bankInfo: hasBankInfo
          ? {
              bankCode: bi.bankCode.trim() || null,
              branch: bi.branch.trim() || null,
              account: bi.account.trim() || null,
              accountDigit: bi.accountDigit.trim() || null,
              pixKey: bi.pixKey.trim() || null,
            }
          : null,
        taxRegime: v.taxRegime,
        cprbForeseen: v.cprbForeseen,
        // Anexo só faz sentido para Simples — zera se o regime não for Simples.
        simplesAnexo: v.taxRegime === 'SimplesNacional' ? v.simplesAnexo : null,
        paymentScheduleMode: v.paymentScheduleMode,
        paymentScheduleDay: v.paymentScheduleMode === 'FixedDayOfMonth' ? v.paymentScheduleDay : null,
        acceptedPaymentMethods: v.acceptedPaymentMethods,
      },
    });
  };

  return (
    <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      {notice && <div className='form-notice'>{notice}</div>}
      <Controller
        control={control}
        name='personType'
        render={({ field }) => <PersonTypeToggle value={field.value} onChange={field.onChange} />}
      />
      <div className='form-grid'>
        <div className='span-2'>
          <Field label={isPj ? 'CNPJ' : 'CPF'} help={formState.errors.document ? undefined : t('forms.docAutoHelp')}>
            <Controller
              control={control}
              name='document'
              render={({ field }) => (
                <MaskedInput
                  value={field.value}
                  mask={isPj ? maskCnpj : maskCpf}
                  onValueChange={field.onChange}
                  onBlur={() => void onCnpjBlur()}
                  className={formState.errors.document ? 'input-err' : undefined}
                  placeholder={isPj ? '00.000.000/0000-00' : '000.000.000-00'}
                  inputMode='numeric'
                />
              )}
            />
            <ErrMsg message={formState.errors.document?.message} />
          </Field>
        </div>
        <div className='span-2'>
          <Field
            label={
              <>
                {isPj ? 'Razão social' : 'Nome'} {nameAuto.has('name') && <AutoHint />}
              </>
            }
          >
            <Controller
              control={control}
              name='name'
              render={({ field }) => (
                <Input className={nameAuto.has('name') ? 'input-auto' : undefined} {...field} autoFocus />
              )}
            />
            <ErrMsg message={formState.errors.name?.message} />
          </Field>
        </div>
        {isPj && (
          <div className='span-2'>
            <Field label={<>Nome fantasia {nameAuto.has('tradeName') && <AutoHint />}</>}>
              <Controller
                control={control}
                name='tradeName'
                render={({ field }) => (
                  <Input className={nameAuto.has('tradeName') ? 'input-auto' : undefined} {...field} />
                )}
              />
            </Field>
          </div>
        )}
        <Field label='E-mail'>
          <Controller control={control} name='email' render={({ field }) => <Input type='email' {...field} />} />
          <ErrMsg message={formState.errors.email?.message} />
        </Field>
        <Field label='Telefone'>
          <Controller
            control={control}
            name='phone'
            render={({ field }) => (
              <MaskedInput
                value={field.value}
                mask={maskPhone}
                onValueChange={field.onChange}
                placeholder='(00) 00000-0000'
                inputMode='tel'
              />
            )}
          />
        </Field>
      </div>

      <AddressFields control={control} setValue={setValue} getAddress={() => getValues('address')} />

      <div className='form-section'>Comercial</div>
      <div className='form-grid'>
        <Field label='Prazo de pagamento'>
          <Controller
            control={control}
            name='paymentTerms'
            render={({ field }) => <Input {...field} placeholder='ex.: 30 dias' />}
          />
        </Field>
        <div className='span-2'>
          <Field label='Condição de pagamento' help='Formas de pagamento aceitas por este fornecedor.'>
            <Controller
              control={control}
              name='acceptedPaymentMethods'
              render={({ field }) => {
                const sel = new Set(field.value);
                const toggle = (m: (typeof PAYMENT_METHODS)[number]) =>
                  field.onChange(PAYMENT_METHODS.filter((x) => (x === m ? !sel.has(m) : sel.has(x))));
                return (
                  <div className='uni-pick'>
                    {PAYMENT_METHODS.map((m) => (
                      <label className='uni-pick-row' key={m}>
                        <input type='checkbox' checked={sel.has(m)} onChange={() => toggle(m)} />{' '}
                        {t(`payments.methods.${m}`)}
                      </label>
                    ))}
                  </div>
                );
              }}
            />
          </Field>
        </div>
        <Field label={t('registry.vendor.taxRegime')}>
          <Controller
            control={control}
            name='taxRegime'
            render={({ field }) => (
              <Select {...field}>
                {TAX_REGIMES.map((r) => (
                  <option key={r} value={r}>
                    {t(`taxRegime.${r}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>
        <Field label={t('registry.vendor.cprbForeseen')}>
          <Controller
            control={control}
            name='cprbForeseen'
            render={({ field }) => (
              <Select
                value={field.value == null ? '' : String(field.value)}
                onChange={(e) => field.onChange(e.target.value === '' ? null : e.target.value === 'true')}
              >
                <option value=''>{t('registry.vendor.cprbUnknown')}</option>
                <option value='true'>{t('registry.vendor.cprbYes')}</option>
                <option value='false'>{t('registry.vendor.cprbNo')}</option>
              </Select>
            )}
          />
        </Field>
        {taxRegime === 'SimplesNacional' && (
          <Field label={t('registry.vendor.simplesAnexo')}>
            <Controller
              control={control}
              name='simplesAnexo'
              render={({ field }) => (
                <Select
                  value={field.value ?? ''}
                  onChange={(e) =>
                    field.onChange(e.target.value === '' ? null : (e.target.value as (typeof SIMPLES_ANEXOS)[number]))
                  }
                >
                  <option value=''>{t('registry.vendor.simplesAnexoNone')}</option>
                  {SIMPLES_ANEXOS.map((a) => (
                    <option key={a} value={a}>
                      Anexo {a}
                    </option>
                  ))}
                </Select>
              )}
            />
          </Field>
        )}
      </div>

      <div className='form-section'>{t('registry.vendor.bankSection')}</div>
      <div className='form-grid'>
        <Field label={t('registry.vendor.bankCode')}>
          <Controller
            control={control}
            name='bankInfo'
            render={({ field }) => (
              <Input
                value={field.value.bankCode}
                onChange={(e) => field.onChange({ ...field.value, bankCode: e.target.value })}
                placeholder='ex.: 001'
                inputMode='numeric'
              />
            )}
          />
        </Field>
        <Field label={t('registry.vendor.branch')}>
          <Controller
            control={control}
            name='bankInfo'
            render={({ field }) => (
              <Input
                value={field.value.branch}
                onChange={(e) => field.onChange({ ...field.value, branch: e.target.value })}
                placeholder='ex.: 0001'
                inputMode='numeric'
              />
            )}
          />
        </Field>
        <Field label={t('registry.vendor.account')}>
          <Controller
            control={control}
            name='bankInfo'
            render={({ field }) => (
              <Input
                value={field.value.account}
                onChange={(e) => field.onChange({ ...field.value, account: e.target.value })}
                placeholder='ex.: 12345'
                inputMode='numeric'
              />
            )}
          />
        </Field>
        <Field label={t('registry.vendor.accountDigit')}>
          <Controller
            control={control}
            name='bankInfo'
            render={({ field }) => (
              <Input
                value={field.value.accountDigit}
                onChange={(e) => field.onChange({ ...field.value, accountDigit: e.target.value })}
                placeholder='ex.: 0'
                maxLength={2}
              />
            )}
          />
        </Field>
        <div className='span-2'>
          <Field label={t('registry.vendor.pixKey')}>
            <Controller
              control={control}
              name='bankInfo'
              render={({ field }) => (
                <Input
                  value={field.value.pixKey}
                  onChange={(e) => field.onChange({ ...field.value, pixKey: e.target.value })}
                  placeholder='CPF, CNPJ, e-mail, telefone ou chave aleatória'
                />
              )}
            />
          </Field>
        </div>
      </div>

      <div className='form-section'>{t('vendors.paymentSchedule.label')}</div>
      <div className='form-grid'>
        <Field label={t('vendors.paymentSchedule.label')}>
          <Controller
            control={control}
            name='paymentScheduleMode'
            render={({ field }) => (
              <Select {...field}>
                {PAYMENT_SCHEDULE_MODES.map((m) => (
                  <option key={m} value={m}>
                    {t(`vendors.paymentSchedule.mode${m}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>
        {paymentScheduleMode === 'FixedDayOfMonth' && (
          <Field label={t('vendors.paymentSchedule.day')}>
            <Controller
              control={control}
              name='paymentScheduleDay'
              render={({ field }) => (
                <Input
                  type='number'
                  min={1}
                  max={28}
                  inputMode='numeric'
                  value={field.value ?? ''}
                  onChange={(e) => field.onChange(e.target.value === '' ? null : Number(e.target.value))}
                />
              )}
            />
            <ErrMsg message={formState.errors.paymentScheduleDay?.message} />
          </Field>
        )}
      </div>

      <FormActions valid={formState.isValid} saving={saving} onCancel={onCancel} editing={!!initial} />
    </form>
  );
}

/* ============================ CUSTOMER ============================ */
const CUSTOMER_FIELDS = ['personType', 'name', 'tradeName', 'document', 'email', 'phone', 'creditLimit'] as const;

export function CustomerForm({
  companyId,
  initial,
  onSaved,
  onCancel,
}: {
  companyId: string;
  initial?: Customer;
  onSaved: (saved: Customer) => void;
  onCancel: () => void;
}) {
  const toast = useToast();
  const { t } = useTranslation();
  const [nameAuto, setNameAuto] = useState<Set<'name' | 'tradeName'>>(new Set());

  const form = useForm<CustomerValues>({
    resolver: resolver(customerSchema),
    mode: 'onChange',
    defaultValues: {
      personType: initial?.personType ?? PersonType.Company,
      name: initial?.name ?? '',
      tradeName: initial?.tradeName ?? '',
      document: initial?.document ?? '',
      email: initial?.email ?? '',
      phone: initial?.phone ?? '',
      creditLimit: initial?.creditLimit != null ? String(initial.creditLimit) : '',
      address: fromAddress(initial?.address ?? null),
    },
  });
  const { control, handleSubmit, setValue, getValues, watch, setError, formState } = form;

  const mutation = useSaveCustomer({
    onSuccess: (saved) => {
      toast.success(initial ? 'Cliente atualizado.' : 'Cliente criado.');
      onSaved(saved);
    },
    onError: (e) => {
      if (!applyServerErrors<CustomerValues>(e, CUSTOMER_FIELDS, setError)) toast.error(getErrorMessage(e));
    },
  });
  const saving = mutation.isPending;
  const personType = watch('personType');
  const isPj = personType === PersonType.Company;

  const onCnpjBlur = async () => {
    const document = getValues('document');
    if (!isPj || onlyDigits(document).length !== 14) return;
    try {
      const r = await api.lookupCnpj(onlyDigits(document));
      const autoFilled = new Set<'name' | 'tradeName'>();
      if (!getValues('name').trim()) {
        setValue('name', r.razaoSocial, { shouldValidate: true });
        autoFilled.add('name');
      }
      if (!getValues('tradeName').trim() && r.nomeFantasia) {
        setValue('tradeName', r.nomeFantasia);
        autoFilled.add('tradeName');
      }
      if (r.address) {
        setValue('address', mergeAddressEmpty(getValues('address'), r.address).address, { shouldValidate: true });
      }
      if (autoFilled.size) setNameAuto(autoFilled);
      toast.success(t('forms.autoFromCnpj'));
    } catch {
      toast.error(t('forms.cnpjNotFound'));
    }
  };

  const onValid = (v: CustomerValues) => {
    const docDigits = onlyDigits(v.document);
    mutation.mutate({
      id: initial?.id,
      body: {
        companyId,
        personType: v.personType,
        name: v.name.trim(),
        tradeName: v.tradeName.trim() || null,
        document: docDigits,
        email: v.email.trim() || null,
        phone: v.phone.trim() || null,
        address: toAddress(v.address),
        creditLimit: v.creditLimit.trim() ? Number(v.creditLimit) : null,
      },
    });
  };

  return (
    <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <Controller
        control={control}
        name='personType'
        render={({ field }) => <PersonTypeToggle value={field.value} onChange={field.onChange} />}
      />
      <div className='form-grid'>
        <div className='span-2'>
          <Field label={isPj ? 'CNPJ' : 'CPF'}>
            <Controller
              control={control}
              name='document'
              render={({ field }) => (
                <MaskedInput
                  value={field.value}
                  mask={isPj ? maskCnpj : maskCpf}
                  onValueChange={field.onChange}
                  onBlur={() => void onCnpjBlur()}
                  className={formState.errors.document ? 'input-err' : undefined}
                  placeholder={isPj ? '00.000.000/0000-00' : '000.000.000-00'}
                  inputMode='numeric'
                />
              )}
            />
            <ErrMsg message={formState.errors.document?.message} />
          </Field>
        </div>
        <div className='span-2'>
          <Field
            label={
              <>
                {isPj ? 'Razão social' : 'Nome'} {nameAuto.has('name') && <AutoHint />}
              </>
            }
          >
            <Controller
              control={control}
              name='name'
              render={({ field }) => (
                <Input className={nameAuto.has('name') ? 'input-auto' : undefined} {...field} autoFocus />
              )}
            />
            <ErrMsg message={formState.errors.name?.message} />
          </Field>
        </div>
        {isPj && (
          <div className='span-2'>
            <Field label={<>Nome fantasia {nameAuto.has('tradeName') && <AutoHint />}</>}>
              <Controller
                control={control}
                name='tradeName'
                render={({ field }) => (
                  <Input className={nameAuto.has('tradeName') ? 'input-auto' : undefined} {...field} />
                )}
              />
            </Field>
          </div>
        )}
        <Field label='E-mail'>
          <Controller control={control} name='email' render={({ field }) => <Input type='email' {...field} />} />
          <ErrMsg message={formState.errors.email?.message} />
        </Field>
        <Field label='Telefone'>
          <Controller
            control={control}
            name='phone'
            render={({ field }) => (
              <MaskedInput
                value={field.value}
                mask={maskPhone}
                onValueChange={field.onChange}
                placeholder='(00) 00000-0000'
                inputMode='tel'
              />
            )}
          />
        </Field>
        <Field label='Limite de crédito (R$)'>
          <Controller
            control={control}
            name='creditLimit'
            render={({ field }) => <Input {...field} inputMode='numeric' placeholder='ex.: 10000' />}
          />
          <ErrMsg message={formState.errors.creditLimit?.message} />
        </Field>
      </div>

      <AddressFields control={control} setValue={setValue} getAddress={() => getValues('address')} />

      <FormActions valid={formState.isValid} saving={saving} onCancel={onCancel} editing={!!initial} />
    </form>
  );
}

/* ============================ EMPLOYEE ============================ */
const EMPLOYEE_FIELDS = ['name', 'document', 'email', 'phone', 'role', 'unitId', 'costCenterId', 'hireDate'] as const;

export function EmployeeForm({
  companyId,
  units,
  costCenters,
  initial,
  onSaved,
  onCancel,
}: {
  companyId: string;
  units: Unit[];
  costCenters: CostCenter[];
  initial?: Employee;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const toast = useToast();

  const form = useForm<EmployeeValues>({
    resolver: resolver(employeeSchema),
    mode: 'onChange',
    defaultValues: {
      name: initial?.name ?? '',
      document: initial?.document ?? '',
      email: initial?.email ?? '',
      phone: initial?.phone ?? '',
      role: initial?.role ?? '',
      unitId: initial?.unitId ?? '',
      costCenterId: initial?.costCenterId ?? '',
      hireDate: initial?.hireDate ?? '',
      address: fromAddress(initial?.address ?? null),
    },
  });
  const { control, handleSubmit, setValue, getValues, setError, formState } = form;

  const mutation = useSaveEmployee({
    onSuccess: () => {
      toast.success(initial ? 'Colaborador atualizado.' : 'Colaborador criado.');
      onSaved();
    },
    onError: (e) => {
      if (!applyServerErrors<EmployeeValues>(e, EMPLOYEE_FIELDS, setError)) toast.error(getErrorMessage(e));
    },
  });
  const saving = mutation.isPending;

  const onValid = (v: EmployeeValues) => {
    mutation.mutate({
      id: initial?.id,
      body: {
        companyId,
        name: v.name.trim(),
        document: onlyDigits(v.document),
        email: v.email.trim() || null,
        phone: v.phone.trim() || null,
        role: v.role.trim(),
        unitId: v.unitId,
        costCenterId: v.costCenterId || null,
        hireDate: v.hireDate,
        address: toAddress(v.address),
      },
    });
  };

  return (
    <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <div className='form-grid'>
        <div className='span-2'>
          <Field label='Nome'>
            <Controller control={control} name='name' render={({ field }) => <Input {...field} autoFocus />} />
            <ErrMsg message={formState.errors.name?.message} />
          </Field>
        </div>
        <Field label='CPF'>
          <Controller
            control={control}
            name='document'
            render={({ field }) => (
              <MaskedInput
                value={field.value}
                mask={maskCpf}
                onValueChange={field.onChange}
                className={formState.errors.document ? 'input-err' : undefined}
                placeholder='000.000.000-00'
                inputMode='numeric'
              />
            )}
          />
          <ErrMsg message={formState.errors.document?.message} />
        </Field>
        <Field label='Cargo'>
          <Controller
            control={control}
            name='role'
            render={({ field }) => <Input {...field} placeholder='ex.: Analista' />}
          />
          <ErrMsg message={formState.errors.role?.message} />
        </Field>
        <Field label='E-mail'>
          <Controller control={control} name='email' render={({ field }) => <Input type='email' {...field} />} />
          <ErrMsg message={formState.errors.email?.message} />
        </Field>
        <Field label='Telefone'>
          <Controller
            control={control}
            name='phone'
            render={({ field }) => (
              <MaskedInput
                value={field.value}
                mask={maskPhone}
                onValueChange={field.onChange}
                placeholder='(00) 00000-0000'
                inputMode='tel'
              />
            )}
          />
        </Field>
        <Field label='Unidade'>
          <Controller
            control={control}
            name='unitId'
            render={({ field }) => (
              <Select {...field}>
                <option value=''>— selecione —</option>
                {units.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.name}
                  </option>
                ))}
              </Select>
            )}
          />
          <ErrMsg message={formState.errors.unitId?.message} />
        </Field>
        <Field label='Centro de custo'>
          <Controller
            control={control}
            name='costCenterId'
            render={({ field }) => (
              <Select {...field}>
                <option value=''>— opcional —</option>
                {costCenters.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>
        <Field label='Data de admissão'>
          <Controller control={control} name='hireDate' render={({ field }) => <Input type='date' {...field} />} />
          <ErrMsg message={formState.errors.hireDate?.message} />
        </Field>
      </div>

      <AddressFields control={control} setValue={setValue} getAddress={() => getValues('address')} />

      <FormActions valid={formState.isValid} saving={saving} onCancel={onCancel} editing={!!initial} />
    </form>
  );
}

/* ---------- small shared bits ---------- */
function PersonTypeToggle({ value, onChange }: { value: PersonType; onChange: (v: PersonType) => void }) {
  return (
    <div className='seg' role='tablist'>
      <button
        type='button'
        className={value === PersonType.Company ? 'on' : ''}
        onClick={() => onChange(PersonType.Company)}
      >
        Pessoa jurídica
      </button>
      <button
        type='button'
        className={value === PersonType.Individual ? 'on' : ''}
        onClick={() => onChange(PersonType.Individual)}
      >
        Pessoa física
      </button>
    </div>
  );
}

export function FormActions({
  valid,
  saving,
  onCancel,
  editing,
}: {
  valid: boolean;
  saving: boolean;
  onCancel: () => void;
  editing: boolean;
}) {
  return (
    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', marginTop: 6 }}>
      <Button type='button' onClick={onCancel} disabled={saving}>
        Cancelar
      </Button>
      <Button type='submit' variant='brand' disabled={!valid || saving}>
        {saving ? 'Salvando…' : editing ? 'Salvar' : 'Criar'}
      </Button>
    </div>
  );
}
