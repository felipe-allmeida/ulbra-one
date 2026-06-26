import { useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, Wallet } from 'lucide-react';
import { PersonType, type Payable, type PayableInput, type ParsedFiscalDocument, type PaymentMethod } from '../../api';
import {
  resolver,
  fieldError,
  payableSchema,
  previewInstallments,
  type PayableValues,
  type VendorValues,
} from '../schemas';
import { useApp } from '../../store';
import {
  useBankAccounts,
  useChartAccounts,
  useCreatePayable,
  usePayable,
  useUpdatePayable,
  useUploadPayableAttachment,
  useUploadPayableFiscalDocument,
} from '../../queries';
import { brl } from '../../lib';
import { getErrorMessage } from '../../apiError';
import { Button, Input, Select, Field, EmptyState, Combobox, FilePicker } from '../../ui';
import { AutoHint } from '../registry-forms';
import { useNames } from './use-names';
import { fmtDate } from '../../datetime';
import { VendorPicker } from './vendor-picker';
import { FiscalXmlReader } from './fiscal-xml-reader';

/* ============ CREATE / EDIT FORM ============ */
function PayableForm({ initial }: { initial?: Payable }) {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { t } = useTranslation();
  const { centros } = useNames(empresaId);
  const accountingAccounts =
    useChartAccounts({ chartType: 'Accounting', kind: 'Analytical', activeOnly: true }).data ?? [];
  const financialAccounts =
    useChartAccounts({ chartType: 'Financial', kind: 'Analytical', activeOnly: true }).data ?? [];
  const accountItems = (accs: typeof accountingAccounts) =>
    [...accs].sort((a, b) => a.code.localeCompare(b.code)).map((a) => ({ id: a.id, label: `${a.code} — ${a.name}` }));
  const bankAccountsList = useBankAccounts(empresaId, false).data ?? [];
  // Per-payable payment source override (null = use company default).
  const [paymentBankAccountId, setPaymentBankAccountId] = useState<string | null>(
    initial?.paymentBankAccountId ?? null,
  );
  const [paymentMethodOverride, setPaymentMethodOverride] = useState<PaymentMethod | null>(
    initial?.paymentMethod ?? null,
  );
  const [file, setFile] = useState<File | null>(null);

  const editing = !!initial;
  // Derive form mode/values from an existing payable (installments → parcelado when >1).
  const fromInitial = (): PayableValues => {
    if (!initial) {
      return {
        vendorId: '',
        costCenterId: '',
        categoryId: null,
        accountingAccountId: null,
        financialAccountId: null,
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
      vendorId: initial.vendorId,
      costCenterId: initial.costCenterId,
      categoryId: initial.categoryId ?? null,
      accountingAccountId: initial.accountingAccountId ?? null,
      financialAccountId: initial.financialAccountId ?? null,
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

  const { control, handleSubmit, watch, setValue, formState } = useForm<PayableValues>({
    resolver: resolver(payableSchema),
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

  const create = useCreatePayable();
  const update = useUpdatePayable();
  const upload = useUploadPayableAttachment();
  const uploadFiscal = useUploadPayableFiscalDocument();
  const [fiscalFile, setFiscalFile] = useState<File | null>(null);
  // Key + metadata from the presigned upload of the NF (threaded from the reader to the save).
  const [fiscalUpload, setFiscalUpload] = useState<{
    key: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
  } | null>(null);
  // Explicit installments (duplicatas) read from the NF; when set, they override the generate spec.
  const [xmlInstallments, setXmlInstallments] = useState<{ dueDate: string; amount: number }[] | null>(null);
  const vendorFromXml = useRef(false);
  const descriptionFromXml = useRef(false);
  // Reveal gate (create flow): the payable fields stay hidden until an XML is parsed or the user opts to fill manually.
  const [xmlApplied, setXmlApplied] = useState(false);
  const [manualMode, setManualMode] = useState(false);
  const revealed = editing || xmlApplied || manualMode;
  // The parsed NF (drives the title, the vendor pre-fill and the "from NF" markers); null in manual/edit.
  const [nf, setNf] = useState<ParsedFiscalDocument | null>(null);
  // Payable form fields that were filled from the NF — shown with an AutoHint + input-auto styling.
  const [nfFields, setNfFields] = useState<Set<string>>(new Set());
  const saving = create.isPending || update.isPending || upload.isPending || uploadFiscal.isPending;
  const erro = create.error
    ? getErrorMessage(create.error)
    : update.error
      ? getErrorMessage(update.error)
      : uploadFiscal.error
        ? getErrorMessage(uploadFiscal.error)
        : null;

  // Resets every field that the NF reader may have filled (back to manual defaults).
  const clearXmlFields = () => {
    setValue('documentNumber', '', { shouldValidate: true });
    setValue('issueDate', '', { shouldValidate: true });
    setValue('totalAmount', '', { shouldValidate: true });
    setValue('firstDueDate', '', { shouldValidate: true });
    setValue('mode', 'single', { shouldValidate: true });
    setValue('installmentCount', '2', { shouldValidate: true });
    setXmlInstallments(null);
    if (vendorFromXml.current) {
      setValue('vendorId', '', { shouldValidate: true });
      vendorFromXml.current = false;
    }
    if (descriptionFromXml.current) {
      setValue('description', '', { shouldValidate: true });
      descriptionFromXml.current = false;
    }
    // Collapse back to the upload hero (unless we're editing an existing payable).
    setXmlApplied(false);
    setManualMode(false);
    setNf(null);
    setNfFields(new Set());
    setFiscalUpload(null);
  };

  // Clears anything from a previous NF, then fills the form from the parsed result (auto, no button).
  // Vendor is filled only when still empty; duplicatas (cobr/dup) become the explicit installments.
  const applyParsed = (p: ParsedFiscalDocument) => {
    clearXmlFields();
    const filled = new Set<string>();
    if (p.documentNumber) {
      setValue('documentNumber', p.documentNumber, { shouldValidate: true, shouldDirty: true });
      filled.add('documentNumber');
    }
    if (p.issueDate) {
      setValue('issueDate', p.issueDate.slice(0, 10), { shouldValidate: true, shouldDirty: true });
      filled.add('issueDate');
    }

    const total = p.totalAmount ?? (p.installments.length ? p.installments.reduce((s, i) => s + i.amount, 0) : null);
    if (total != null) {
      setValue('totalAmount', String(total), { shouldValidate: true, shouldDirty: true });
      filled.add('totalAmount');
    }

    if (p.matchedVendorId && !watch('vendorId')) {
      setValue('vendorId', p.matchedVendorId, { shouldValidate: true, shouldDirty: true });
      vendorFromXml.current = true;
      filled.add('vendorId');
    }

    // Description from the NF (NF-e: produtos; NFS-e: discriminação), only while the field is still empty.
    // Capped at 300 to match the payable description column (varchar(300)).
    if (p.serviceDescription && !watch('description')) {
      setValue('description', p.serviceDescription.slice(0, 300), { shouldValidate: true, shouldDirty: true });
      descriptionFromXml.current = true;
      filled.add('description');
    }

    setNf(p);
    setNfFields(filled);

    if (p.installments.length > 1) {
      setValue('mode', 'installments', { shouldValidate: true, shouldDirty: true });
      setValue('installmentCount', String(p.installments.length), { shouldValidate: true });
      setValue('firstDueDate', p.installments[0].dueDate, { shouldValidate: true, shouldDirty: true });
      setXmlInstallments(p.installments);
    } else if (p.installments.length === 1) {
      setValue('firstDueDate', p.installments[0].dueDate, { shouldValidate: true, shouldDirty: true });
    }
    // installments.length === 0 → à vista, vencimento em branco (já limpo acima).
    setXmlApplied(true);
  };

  const buildBody = (v: PayableValues): Omit<PayableInput, 'origin'> => {
    const base = {
      companyId: empresaId,
      vendorId: v.vendorId,
      costCenterId: v.costCenterId,
      categoryId: v.categoryId || null,
      accountingAccountId: v.accountingAccountId || null,
      financialAccountId: v.financialAccountId || null,
      description: v.description.trim(),
      documentNumber: v.documentNumber.trim() || null,
      issueDate: v.issueDate || null,
      paymentBankAccountId: paymentBankAccountId ?? null,
      paymentMethod: paymentMethodOverride ?? null,
    };
    // Duplicatas da NF → parcelas explícitas (datas/valores reais), sem passar pelo generate.
    if (xmlInstallments && xmlInstallments.length > 0) {
      return { ...base, installments: xmlInstallments };
    }
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

  const onValid = async (v: PayableValues) => {
    const body = buildBody(v);
    try {
      const p =
        editing && initial ? await update.mutateAsync({ id: initial.id, body }) : await create.mutateAsync(body);
      // Persist the fiscal document of record (NF) using the key from the presigned upload, then any generic attachment, then navigate.
      if (fiscalUpload) await uploadFiscal.mutateAsync({ id: p.id, ...fiscalUpload });
      if (!editing && file) await upload.mutateAsync({ id: p.id, file });
      nav(`/app/payables/${p.id}`);
    } catch {
      // Errors surface via the mutation hooks' `error` state (see `erro`).
    }
  };

  const backTo = editing && initial ? `/app/payables/${initial.id}` : '/app/payables';

  // When the NF issuer has no matching vendor, build a pre-fill so the user can register it in one click.
  const vendorPrefill: Partial<VendorValues> | undefined =
    nf && !nf.matchedVendorId && nf.issuerDocument
      ? {
          personType: nf.issuerDocument.length === 14 ? PersonType.Company : PersonType.Individual,
          name: nf.issuerName ?? '',
          document: nf.issuerDocument,
          phone: nf.issuerPhone ?? '',
          address: nf.issuerAddress
            ? {
                cep: nf.issuerAddress.cep ?? '',
                street: nf.issuerAddress.street ?? '',
                number: nf.issuerAddress.number ?? '',
                complement: '',
                neighborhood: nf.issuerAddress.neighborhood ?? '',
                city: nf.issuerAddress.city ?? '',
                state: nf.issuerAddress.state ?? '',
              }
            : undefined,
        }
      : undefined;
  const vendorNotice = vendorPrefill ? t('payables.fiscalReader.vendorNoticeBanner') : undefined;

  return (
    <>
      <button className='backlink' onClick={() => nav(backTo)}>
        <ChevronLeft /> {t('common.back')}
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{editing ? t('payables.editTitle') : t('payables.newTitle')}</h1>
          <div className='detail-sub'>{empresa?.legalName ?? t('payables.empty.noCompanyForm')}</div>
        </div>
      </div>

      {!empresa ? (
        <EmptyState
          icon={<Wallet />}
          title={t('payables.empty.noCompanyTitle')}
          sub={t('payables.empty.noCompanyForm')}
        />
      ) : (
        <div>
          <FiscalXmlReader
            companyId={empresaId}
            file={fiscalFile}
            onFile={setFiscalFile}
            onApply={applyParsed}
            onUploadKey={(key, meta) => setFiscalUpload({ key, ...meta })}
            onClear={clearXmlFields}
            variant={revealed ? 'band' : 'hero'}
          />
          {!revealed && (
            <div style={{ maxWidth: 560, margin: '12px auto 0', textAlign: 'center' }}>
              <button type='button' className='linklike' onClick={() => setManualMode(true)}>
                {t('payables.fiscalReader.fillManually')}
              </button>
            </div>
          )}
          {revealed && (
            <form
              className='card-form'
              style={{ maxWidth: 'none', marginTop: 16 }}
              onSubmit={(e) => void handleSubmit(onValid)(e)}
              noValidate
            >
              {!editing && (
                <div style={{ marginBottom: 14 }}>
                  <div className='section-title' style={{ marginTop: 0, marginBottom: 2 }}>
                    {t('payables.confirmSection.title')}
                  </div>
                  <div className='help'>{t('payables.confirmSection.hint')}</div>
                </div>
              )}
              <div className='edit-grid'>
                <Field
                  label={
                    <>
                      {t('payables.fields.vendor')} {nfFields.has('vendorId') && <AutoHint />}
                    </>
                  }
                >
                  <Controller
                    control={control}
                    name='vendorId'
                    render={({ field }) => (
                      <VendorPicker
                        companyId={empresaId}
                        value={field.value || null}
                        onChange={(id) => field.onChange(id ?? '')}
                        invalid={!!formState.errors.vendorId}
                        prefill={vendorPrefill}
                        notice={vendorNotice}
                      />
                    )}
                  />
                  {fieldError(formState.errors.vendorId?.message) && (
                    <div className='field-err'>{fieldError(formState.errors.vendorId?.message)}</div>
                  )}
                </Field>
                <Field label={t('payables.fields.costCenter')}>
                  <Controller
                    control={control}
                    name='costCenterId'
                    render={({ field }) => (
                      <Select {...field}>
                        <option value=''>{t('payables.fields.costCenterSelect')}</option>
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
                <Field label={t('payables.accountingAccount')}>
                  <Controller
                    control={control}
                    name='accountingAccountId'
                    render={({ field }) => (
                      <Combobox
                        value={field.value ?? null}
                        onChange={(id) => field.onChange(id)}
                        items={accountItems(accountingAccounts)}
                        placeholder={t('payables.fields.accountNone')}
                      />
                    )}
                  />
                </Field>
                <Field label={t('payables.financialAccount')}>
                  <Controller
                    control={control}
                    name='financialAccountId'
                    render={({ field }) => (
                      <Combobox
                        value={field.value ?? null}
                        onChange={(id) => field.onChange(id)}
                        items={accountItems(financialAccounts)}
                        placeholder={t('payables.fields.accountNone')}
                      />
                    )}
                  />
                </Field>
                <Field
                  label={
                    <>
                      {t('payables.fields.documentNumber')} {nfFields.has('documentNumber') && <AutoHint />}
                    </>
                  }
                >
                  <Controller
                    control={control}
                    name='documentNumber'
                    render={({ field }) => (
                      <Input
                        className={nfFields.has('documentNumber') ? 'input-auto' : undefined}
                        {...field}
                        placeholder={t('payables.placeholders.documentNumber')}
                      />
                    )}
                  />
                </Field>
                <Field
                  label={
                    <>
                      {t('payables.fields.issueDate')} {nfFields.has('issueDate') && <AutoHint />}
                    </>
                  }
                >
                  <Controller
                    control={control}
                    name='issueDate'
                    render={({ field }) => (
                      <Input type='date' className={nfFields.has('issueDate') ? 'input-auto' : undefined} {...field} />
                    )}
                  />
                </Field>
              </div>

              <div style={{ marginTop: 14 }}>
                <Field
                  label={
                    <>
                      {t('payables.fields.description')} {nfFields.has('description') && <AutoHint />}
                    </>
                  }
                >
                  <Controller
                    control={control}
                    name='description'
                    render={({ field }) => (
                      <Input
                        className={nfFields.has('description') ? 'input-auto' : undefined}
                        maxLength={300}
                        {...field}
                        placeholder={t('payables.placeholders.description')}
                      />
                    )}
                  />
                  {fieldError(formState.errors.description?.message) && (
                    <div className='field-err'>{fieldError(formState.errors.description?.message)}</div>
                  )}
                </Field>
              </div>

              {/* ——— Pagar de (optional payment source override) ——— */}
              <div style={{ marginTop: 14 }}>
                <div className='section-title' style={{ marginTop: 0, marginBottom: 2 }}>
                  {t('payables.paymentSource.sectionTitle')}
                </div>
                <div className='help' style={{ marginBottom: 10 }}>
                  {t('payables.paymentSource.sectionHint')}
                </div>
                <div className='edit-grid'>
                  <Field label={t('payables.paymentSource.bankAccount')}>
                    <Select
                      value={paymentBankAccountId ?? ''}
                      onChange={(e) => {
                        setPaymentBankAccountId(e.target.value !== '' ? e.target.value : null);
                        if (!e.target.value) setPaymentMethodOverride(null);
                      }}
                    >
                      <option value=''>{t('payables.paymentSource.bankAccountSelect')}</option>
                      {bankAccountsList.map((b) => (
                        <option key={b.id} value={b.id}>
                          {b.alias} — {b.bankName}
                        </option>
                      ))}
                    </Select>
                  </Field>
                  {paymentBankAccountId && (
                    <Field label={t('payables.paymentSource.method')}>
                      <Select
                        value={paymentMethodOverride ?? ''}
                        onChange={(e) =>
                          setPaymentMethodOverride(e.target.value !== '' ? (e.target.value as PaymentMethod) : null)
                        }
                      >
                        <option value=''>{t('payables.paymentSource.methodSelect')}</option>
                        <option value='Pix'>{t('payments.methods.Pix')}</option>
                        <option value='Ted'>{t('payments.methods.Ted')}</option>
                        <option value='Boleto'>{t('payments.methods.Boleto')}</option>
                        <option value='Cash'>{t('payments.methods.Cash')}</option>
                        <option value='DirectDebit'>{t('payments.methods.DirectDebit')}</option>
                        <option value='Other'>{t('payments.methods.Other')}</option>
                      </Select>
                    </Field>
                  )}
                </div>
              </div>

              <div style={{ marginTop: 14 }}>
                <Field label={t('payables.fields.mode')}>
                  <Controller
                    control={control}
                    name='mode'
                    render={({ field }) => (
                      <div style={{ display: 'flex', gap: 8 }}>
                        <Button
                          type='button'
                          variant={field.value === 'single' ? 'brand' : 'default'}
                          onClick={() => {
                            field.onChange('single');
                            setXmlInstallments(null);
                          }}
                        >
                          {t('payables.mode.single')}
                        </Button>
                        <Button
                          type='button'
                          variant={field.value === 'installments' ? 'brand' : 'default'}
                          onClick={() => {
                            field.onChange('installments');
                            setXmlInstallments(null);
                          }}
                        >
                          {t('payables.mode.installments')}
                        </Button>
                      </div>
                    )}
                  />
                </Field>
              </div>

              <div className='edit-grid' style={{ marginTop: 14 }}>
                <Field
                  label={
                    <>
                      {mode === 'single' ? t('payables.fields.amount') : t('payables.fields.totalAmount')}{' '}
                      {nfFields.has('totalAmount') && <AutoHint />}
                    </>
                  }
                >
                  <Controller
                    control={control}
                    name='totalAmount'
                    render={({ field }) => (
                      <Input
                        className={nfFields.has('totalAmount') ? 'input-auto' : undefined}
                        {...field}
                        inputMode='numeric'
                        placeholder={t('payables.placeholders.amount')}
                      />
                    )}
                  />
                  {fieldError(formState.errors.totalAmount?.message) && (
                    <div className='field-err'>{fieldError(formState.errors.totalAmount?.message)}</div>
                  )}
                </Field>
                {!xmlInstallments && (
                  <Field label={mode === 'single' ? t('payables.fields.dueDate') : t('payables.fields.firstDueDate')}>
                    <Controller
                      control={control}
                      name='firstDueDate'
                      render={({ field }) => <Input type='date' {...field} />}
                    />
                    {fieldError(formState.errors.firstDueDate?.message) && (
                      <div className='field-err'>{fieldError(formState.errors.firstDueDate?.message)}</div>
                    )}
                  </Field>
                )}
                {mode === 'installments' && !xmlInstallments && (
                  <>
                    <Field label={t('payables.fields.installmentCount')}>
                      <Controller
                        control={control}
                        name='installmentCount'
                        render={({ field }) => <Input {...field} inputMode='numeric' />}
                      />
                      {fieldError(formState.errors.installmentCount?.message) && (
                        <div className='field-err'>{fieldError(formState.errors.installmentCount?.message)}</div>
                      )}
                    </Field>
                    <Field label={t('payables.fields.intervalMonths')}>
                      <Controller
                        control={control}
                        name='intervalMonths'
                        render={({ field }) => <Input {...field} inputMode='numeric' />}
                      />
                    </Field>
                  </>
                )}
              </div>

              {mode === 'installments' && !xmlInstallments && preview.length > 0 && (
                <div className='panel' style={{ marginTop: 14 }}>
                  <div className='panel-head'>
                    <span className='panel-title'>{t('payables.preview.title')}</span>
                  </div>
                  <table>
                    <thead>
                      <tr>
                        <th>{t('payables.preview.installment')}</th>
                        <th>{t('payables.preview.dueDate')}</th>
                        <th style={{ textAlign: 'right' }}>{t('payables.preview.amount')}</th>
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

              {xmlInstallments && xmlInstallments.length > 0 && (
                <div className='panel' style={{ marginTop: 14 }}>
                  <div className='panel-head'>
                    <span className='panel-title'>{t('payables.fiscalReader.installmentsFromNf')}</span>
                  </div>
                  <table>
                    <thead>
                      <tr>
                        <th>{t('payables.preview.installment')}</th>
                        <th>{t('payables.preview.dueDate')}</th>
                        <th style={{ textAlign: 'right' }}>{t('payables.preview.amount')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {xmlInstallments.map((p, i) => (
                        <tr key={`${p.dueDate}-${i}`}>
                          <td className='t-mono'>
                            {i + 1}/{xmlInstallments.length}
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

              {!editing && (
                <div style={{ marginTop: 14 }}>
                  <Field label={t('payables.upload.label')} help={t('payables.upload.help')}>
                    <FilePicker
                      accept='application/pdf,image/png,image/jpeg'
                      fileName={file?.name}
                      placeholder={t('common.noFileSelected')}
                      onSelect={setFile}
                    />
                  </Field>
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
                    ? t('payables.actions.saving')
                    : editing
                      ? t('payables.actions.save')
                      : t('payables.actions.create')}
                </Button>
                <Button type='button' onClick={() => nav(backTo)}>
                  {t('payables.actions.cancel')}
                </Button>
              </div>
            </form>
          )}
        </div>
      )}
    </>
  );
}

export function NewPayablePage() {
  return <PayableForm />;
}

export function EditPayablePage() {
  const { id } = useParams();
  const query = usePayable(id);
  const { t } = useTranslation();
  if (query.isLoading) return <div className='empty-state'>{t('payables.loading')}</div>;
  if (!query.data)
    return (
      <EmptyState icon={<Wallet />} title={t('payables.empty.notFoundTitle')} sub={t('payables.empty.notFoundSub')} />
    );
  return <PayableForm initial={query.data} />;
}
