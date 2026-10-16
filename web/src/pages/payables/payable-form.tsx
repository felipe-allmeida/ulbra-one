import { useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, Wallet, Paperclip } from 'lucide-react';
import { PersonType, type Payable, type PayableInput, type ParsedFiscalDocument } from '../../api';
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
  useLedgerAccounts,
  useCreatePayable,
  usePayable,
  useUpdatePayable,
  useVendor,
  useUploadPayableAttachment,
  useUploadPayableFiscalDocument,
} from '../../queries';
import { brl, moneyInput, parseMoneyInput } from '../../lib';
import { onlyDigits } from '../../format';
import { getErrorMessage } from '../../apiError';
import { P, usePermissions } from '../../permissions';
import { Button, CurrencyInput, Input, Select, Field, EmptyState, Combobox, FilePicker, LoadingState } from '../../ui';
import { AutoHint } from '../registry-forms';
import { useNames } from './use-names';
import { fmtDate } from '../../datetime';
import { VendorPicker } from './vendor-picker';
import { FiscalXmlReader } from './fiscal-xml-reader';
import { PayableIntakeChoice, FiscalIntakeChoice, originForIntake, type PayableIntakeMode } from './intake-choice';

/* ============ CREATE / EDIT FORM ============ */
function PayableForm({ initial }: { initial?: Payable }) {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const location = useLocation();
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canWrite = can(P.payablesWrite);
  const { centros } = useNames(empresaId);
  // Full analytical list (shared, cached app-wide). The payable dropdown lists only expense accounts
  // (roots 4/5 = AccountGroup.Expense); the full list stays available to keep a legacy selected account visible.
  const allAccounts = useLedgerAccounts({ kind: 'Analytical', activeOnly: true }).data ?? [];
  const expenseAccounts = allAccounts.filter((a) => a.accountGroup === 'Expense');
  const bankAccountsList = useBankAccounts(empresaId, false).data ?? [];
  // Per-payable payment source override (null = use company default). The payment method is no longer
  // chosen here — it's decided at settlement, from the methods the vendor accepts (AEL-712).
  const [paymentBankAccountId, setPaymentBankAccountId] = useState<string | null>(
    initial?.paymentBankAccountId ?? null,
  );
  // Type (Service/Product): drives the automatic withholding. Set here so it's applied before any auto-approval.
  const [payableType, setPayableType] = useState<'Service' | 'Product' | null>(initial?.type ?? null);
  // Tipo de fornecimento é obrigatório; erro mostrado após uma tentativa de submit sem selecionar.
  const [typeError, setTypeError] = useState(false);
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
        description: '',
        documentNumber: '',
        issueDate: '',
        boletoDigitableLine: '',
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
      description: initial.description,
      documentNumber: initial.documentNumber ?? '',
      issueDate: initial.issueDate ?? '',
      // Echoed by the installment response (AEL-881) — pre-fill from the single installment's boleto.
      boletoDigitableLine: parcelas[0]?.boletoDigitableLine ?? '',
      mode: parcelado ? 'installments' : 'single',
      totalAmount: moneyInput(initial.totalAmount),
      installmentCount: String(Math.max(2, parcelas.length)),
      firstDueDate: parcelas[0]?.dueDate ?? '',
      intervalMonths: String(interval || 1),
    };
  };

  const { control, handleSubmit, watch, setValue, getValues, formState } = useForm<PayableValues>({
    resolver: resolver(payableSchema),
    mode: 'onChange',
    defaultValues: fromInitial(),
  });
  const mode = watch('mode');
  const totalAmount = watch('totalAmount');
  const installmentCount = watch('installmentCount');
  const firstDueDate = watch('firstDueDate');
  const intervalMonths = watch('intervalMonths');
  const vendorIdValue = watch('vendorId');
  const accountingAccountValue = watch('accountingAccountId');
  // Selected vendor's default expense accounts — pinned to the top of the dropdown and auto-selected when unique.
  const selectedVendor = useVendor(vendorIdValue || undefined).data;
  const vendorAccountIds = useMemo(() => new Set(selectedVendor?.associatedLedgerAccountIds ?? []), [selectedVendor]);
  const accountItems = useMemo(() => {
    const base = [...expenseAccounts];
    // Keep a currently-selected account visible even if it falls outside the expense filter (legacy edit).
    if (accountingAccountValue && !base.some((a) => a.id === accountingAccountValue)) {
      const found = allAccounts.find((a) => a.id === accountingAccountValue);
      if (found) base.push(found);
    }
    const vendorTag = t('payables.fields.accountFromVendor');
    return base
      .sort((a, b) => {
        const av = vendorAccountIds.has(a.id) ? 0 : 1;
        const bv = vendorAccountIds.has(b.id) ? 0 : 1;
        return av !== bv ? av - bv : a.code.localeCompare(b.code);
      })
      .map((a) => ({
        id: a.id,
        label: `${a.code} — ${a.name}`,
        sub: vendorAccountIds.has(a.id) ? vendorTag : undefined,
      }));
  }, [expenseAccounts, allAccounts, accountingAccountValue, vendorAccountIds, t]);
  // Auto-select the vendor's account when it has exactly one and the field is still empty.
  useEffect(() => {
    const ids = selectedVendor?.associatedLedgerAccountIds ?? [];
    if (ids.length === 1 && !getValues('accountingAccountId')) {
      setValue('accountingAccountId', ids[0], { shouldValidate: true, shouldDirty: true });
    }
  }, [selectedVendor, getValues, setValue]);

  const preview = useMemo(() => {
    if (mode !== 'installments') return [];
    const total = parseMoneyInput(totalAmount) ?? 0;
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
  // Intake modality (create flow): chooser first; 'fiscal' nests the reader + manual sub-path,
  // 'advance' (adiantamento) goes straight to the manual form with no NF.
  const [intake, setIntake] = useState<'choice' | PayableIntakeMode>('choice');
  // Fiscal modality sub-step: 'choice' shows the upload-vs-manual blocks; 'upload' processes the picked NF.
  const [fiscalEntry, setFiscalEntry] = useState<'choice' | 'upload'>('choice');
  // File picked straight from the "Enviar arquivo" block — handed to the reader to parse (no extra screen).
  const [autoNfFile, setAutoNfFile] = useState<File | null>(null);
  // Reveal gate (fiscal mode): the payable fields stay hidden until an XML is parsed or the user opts to fill manually.
  const [xmlApplied, setXmlApplied] = useState(false);
  const [manualMode, setManualMode] = useState(false);
  // Picking a modality from the 3-card chooser; the fiscal modality restarts at its own sub-choice.
  const pickIntake = (m: PayableIntakeMode) => {
    setIntake(m);
    if (m === 'fiscal') {
      setFiscalEntry('choice');
      setAutoNfFile(null);
    }
  };
  // "Enviar arquivo": go straight to processing the picked file (the reader parses it), no upload screen.
  const pickFiscalFile = (f: File) => {
    setFiscalEntry('upload');
    setAutoNfFile(f);
  };
  const revealed = editing || intake === 'advance' || (intake === 'fiscal' && (xmlApplied || manualMode));
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
    // Reset the type to its base state (null on create; the original value when editing).
    setPayableType(initial?.type ?? null);
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
      setValue('totalAmount', moneyInput(total), { shouldValidate: true, shouldDirty: true });
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

    // Type (service/product): authoritative from the document format for XML (NFS-e → Service,
    // NF-e → Product). For a PDF/image, use the AI extractor's suggested type.
    const derivedType =
      p.format === 'NfseXml' ? 'Service' : p.format === 'NfeXml' ? 'Product' : (p.suggestedType ?? null);
    if (derivedType) {
      setPayableType(derivedType);
      filled.add('type');
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

  const buildBody = (v: PayableValues, type: 'Service' | 'Product'): Omit<PayableInput, 'origin'> => {
    const base = {
      companyId: empresaId,
      vendorId: v.vendorId,
      costCenterId: v.costCenterId,
      categoryId: v.categoryId || null,
      accountingAccountId: v.accountingAccountId || null,
      description: v.description.trim(),
      documentNumber: v.documentNumber.trim() || null,
      issueDate: v.issueDate || null,
      paymentBankAccountId: paymentBankAccountId ?? null,
      // Preserve the existing method when editing; never set on creation (decided at settlement — AEL-712).
      paymentMethod: initial?.paymentMethod ?? null,
      type,
    };
    // Duplicatas da NF → parcelas explícitas (datas/valores reais), sem passar pelo generate.
    if (xmlInstallments && xmlInstallments.length > 0) {
      return { ...base, installments: xmlInstallments };
    }
    if (v.mode === 'single') {
      // Attach the boleto digitable line only when informed (à vista); keeps the payload clean otherwise.
      const boletoLine = onlyDigits(v.boletoDigitableLine);
      return {
        ...base,
        installments: [
          {
            dueDate: v.firstDueDate,
            amount: parseMoneyInput(v.totalAmount) ?? 0,
            ...(boletoLine ? { boletoDigitableLine: boletoLine } : {}),
          },
        ],
      };
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

  const onValid = async (v: PayableValues) => {
    // Tipo de fornecimento é obrigatório — não faz parte do schema RHF (é state próprio), então guardamos aqui.
    if (!payableType) {
      setTypeError(true);
      return;
    }
    const body = buildBody(v, payableType);
    try {
      // Origin is set at create time from the intake modality; immutable on edit.
      const p =
        editing && initial
          ? await update.mutateAsync({ id: initial.id, body })
          : await create.mutateAsync({ ...body, origin: originForIntake(intake, xmlApplied) });
      // Persist the parsed fiscal document, then attach the NF file so it shows in Anexos and clears the NF gate.
      // fiscalFile (read at the top) and file (the manual "Anexar NF" picker) are alternatives for the same NF —
      // both marked FiscalDocument. Then navigate.
      if (fiscalUpload) await uploadFiscal.mutateAsync({ id: p.id, ...fiscalUpload });
      if (!editing && fiscalFile) await upload.mutateAsync({ id: p.id, file: fiscalFile, kind: 'FiscalDocument' });
      if (file) await upload.mutateAsync({ id: p.id, file, kind: 'FiscalDocument' });
      // Carry the origin forward so the detail's back still points at the list the user came from.
      nav(`/app/payables/${p.id}`, { state: location.state });
    } catch {
      // Errors surface via the mutation hooks' `error` state (see `erro`).
    }
  };

  const backTo = editing && initial ? `/app/payables/${initial.id}` : '/app/payables';
  // From inside a modality, the back link returns to the chooser (not the list); resets any NF state.
  const backToChoice = () => {
    setFiscalFile(null);
    setAutoNfFile(null);
    clearXmlFields();
    setFiscalEntry('choice');
    setIntake('choice');
  };
  // Back link: while a picked NF is still being read (not revealed yet), step back to the fiscal sub-choice;
  // from a modality, to the 3-card chooser; otherwise leave the form. Edit always leaves to the payable.
  const goBack = () => {
    if (editing) return nav(backTo, { state: location.state });
    if (intake === 'fiscal' && fiscalEntry === 'upload' && !revealed) {
      setFiscalFile(null);
      setAutoNfFile(null);
      clearXmlFields();
      setFiscalEntry('choice');
      return;
    }
    if (intake !== 'choice') return backToChoice();
    nav(backTo);
  };

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
      <button className='backlink' onClick={goBack}>
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
      ) : !editing && intake === 'choice' ? (
        <PayableIntakeChoice onPick={pickIntake} />
      ) : (
        <div>
          {/* NF reader (band): while the picked NF is read, once the form is revealed, and in edit.
              Not shown on the fiscal sub-choice screen. An adiantamento has no NF. */}
          {(editing || (intake === 'fiscal' && (fiscalEntry === 'upload' || revealed))) && (
            <FiscalXmlReader
              companyId={empresaId}
              file={fiscalFile}
              onFile={setFiscalFile}
              onApply={applyParsed}
              onUploadKey={(key, meta) => setFiscalUpload({ key, ...meta })}
              onClear={clearXmlFields}
              variant='band'
              autoFile={autoNfFile}
            />
          )}
          {/* Fiscal sub-choice: "Enviar arquivo" (opens the file picker directly) vs. "Preencher manualmente". */}
          {!revealed && intake === 'fiscal' && fiscalEntry === 'choice' && (
            <FiscalIntakeChoice onUpload={pickFiscalFile} onManual={() => setManualMode(true)} />
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
              <div className='edit-grid cols-3'>
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
                <Field
                  label={
                    <>
                      {t('payables.fields.nature')} {nfFields.has('type') && <AutoHint />}
                    </>
                  }
                >
                  <Select
                    value={payableType === 'Service' ? 'service' : payableType === 'Product' ? 'product' : ''}
                    onChange={(e) => {
                      const v = e.target.value;
                      setPayableType(v === 'service' ? 'Service' : v === 'product' ? 'Product' : null);
                    }}
                  >
                    <option value=''>{t('payables.fields.natureSelect')}</option>
                    <option value='service'>{t('payables.fields.natureService')}</option>
                    <option value='product'>{t('payables.fields.natureProduct')}</option>
                  </Select>
                  {typeError && !payableType && <div className='field-err'>{t('validation.typeRequired')}</div>}
                </Field>
              </div>
              <div className='edit-grid' style={{ marginTop: 14 }}>
                <Field label={t('payables.accountingAccount')}>
                  <Controller
                    control={control}
                    name='accountingAccountId'
                    render={({ field }) => (
                      <Combobox
                        value={field.value ?? null}
                        onChange={(id) => field.onChange(id)}
                        items={accountItems}
                        placeholder={t('payables.fields.accountNone')}
                      />
                    )}
                  />
                </Field>
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

              <div className='edit-grid' style={{ marginTop: 14 }}>
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
                <Field label={t('payables.paymentSource.bankAccount')}>
                  <Select
                    value={paymentBankAccountId ?? ''}
                    onChange={(e) => setPaymentBankAccountId(e.target.value !== '' ? e.target.value : null)}
                  >
                    <option value=''>{t('payables.paymentSource.bankAccountSelect')}</option>
                    {bankAccountsList.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.alias} — {b.bankName}
                      </option>
                    ))}
                  </Select>
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
                      <CurrencyInput
                        className={nfFields.has('totalAmount') ? 'input-auto' : undefined}
                        value={field.value}
                        onValueChange={field.onChange}
                        onBlur={field.onBlur}
                        name={field.name}
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

              {/* Linha digitável do boleto (à vista): alimenta a remessa CNAB. O método é escolhido na baixa
                  (AEL-712), então o campo é opcional e sempre disponível no modo à vista. */}
              {mode === 'single' && !xmlInstallments && (
                <div style={{ marginTop: 14 }}>
                  <Field
                    label={t('payables.fields.boletoDigitableLine')}
                    help={t('payables.fields.boletoDigitableLineHelp')}
                  >
                    <Controller
                      control={control}
                      name='boletoDigitableLine'
                      render={({ field }) => (
                        <Input
                          {...field}
                          inputMode='numeric'
                          placeholder={t('payables.placeholders.boletoDigitableLine')}
                          // Só dígitos ao digitar/colar (remove pontos, espaços e separadores da linha digitável).
                          onChange={(e) => field.onChange(onlyDigits(e.target.value))}
                        />
                      )}
                    />
                  </Field>
                </div>
              )}

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

              {/* Linha final: Nº do documento · Data de emissão · Anexar NF (Anexar NF também na edição). */}
              <div className='edit-grid cols-3' style={{ marginTop: 14 }}>
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
                <Field
                  label={t('payables.upload.label')}
                  help={fiscalFile ? t('payables.upload.fromReader') : t('payables.upload.help')}
                >
                  {fiscalFile ? (
                    // The NF read at the top is attached — show it here (no second upload).
                    <div
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 8,
                        minWidth: 0,
                        padding: '10px 12px',
                        border: '1px solid var(--border)',
                        borderRadius: 'var(--radius)',
                        background: 'var(--surface)',
                      }}
                    >
                      <Paperclip style={{ width: 14, height: 14, flexShrink: 0, color: 'var(--brand)' }} />
                      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {fiscalFile.name}
                      </span>
                    </div>
                  ) : (
                    <FilePicker
                      accept='application/pdf,image/png,image/jpeg'
                      fileName={file?.name}
                      placeholder={t('common.noFileSelected')}
                      onSelect={setFile}
                    />
                  )}
                </Field>
              </div>

              {erro && (
                <div className='create-err' style={{ marginTop: 10 }}>
                  {erro}
                </div>
              )}
              <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
                {canWrite && (
                  <Button type='submit' variant='brand' disabled={!formState.isValid || saving}>
                    {saving
                      ? t('payables.actions.saving')
                      : editing
                        ? t('payables.actions.save')
                        : t('payables.actions.create')}
                  </Button>
                )}
                <Button type='button' onClick={() => nav(backTo)}>
                  {canWrite ? t('payables.actions.cancel') : t('payables.actions.close')}
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
  if (query.isLoading) return <LoadingState />;
  if (!query.data)
    return (
      <EmptyState icon={<Wallet />} title={t('payables.empty.notFoundTitle')} sub={t('payables.empty.notFoundSub')} />
    );
  return <PayableForm initial={query.data} />;
}
