import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import {
  Plus,
  ChevronLeft,
  Check,
  X,
  Wallet,
  Pencil,
  Download,
  Trash2,
  Paperclip,
  Receipt,
  Undo2,
  Upload,
} from 'lucide-react';
import {
  api,
  auth,
  PersonType,
  type Payable,
  type PayableInput,
  type PayableStatus,
  type Installment,
  type InstallmentStatus,
  type PayableAttachment,
  type ParsedFiscalDocument,
  type Payment,
  type PaymentMethod,
  type RegisterPaymentInput,
} from '../api';
import { getApiBaseUrl } from '../config/env';
import {
  resolver,
  fieldError,
  payableSchema,
  paymentSchema,
  previewInstallments,
  type PayableValues,
  type PaymentValues,
  type VendorValues,
} from './schemas';
import { useApp } from '../store';
import {
  useApprovePayable,
  useBankAccounts,
  useCancelPayable,
  useChartAccounts,
  useCostCenters,
  useCreatePayable,
  useDeletePayableAttachment,
  useExpenseCategories,
  useParseFiscalDocument,
  usePayable,
  usePayables,
  useRegisterPayment,
  useRejectPayable,
  useRequestFiscalUploadUrl,
  useReversePayment,
  useUpdatePayable,
  useUploadPayableAttachment,
  useUploadPayableFiscalDocument,
  useUploadPaymentProof,
  useVendors,
  uploadToPresignedUrl,
  type PayablesParams,
} from '../queries';
import { brl } from '../lib';
import { maskCnpj, maskCpf } from '../format';
import { getErrorMessage } from '../apiError';
import {
  Button,
  Input,
  Select,
  Field,
  PageHeader,
  Pager,
  Panel,
  EmptyState,
  Combobox,
  Drawer,
  ConfirmDialog,
} from '../ui';
import { EmpresaPicker } from './shared';
import { VendorForm, AutoHint } from './registry-forms';

const fmtData = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');
/** Formats a digits-only CPF/CNPJ for display (14 digits → CNPJ mask, else CPF). */
const fmtDoc = (doc: string) => (doc.length > 11 ? maskCnpj(doc) : maskCpf(doc));
/** Formats an ISO date-time (e.g. approvedAt/rejectedAt) as a pt-BR date. */
const fmtDateTime = (iso: string) => new Date(iso).toLocaleDateString('pt-BR');

/** Local date → 'YYYY-MM-DD' (DateOnly wire format). Never toISOString() — UTC-3 would shift the day. */
const toDateOnly = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

type TabKey = 'all' | 'overdue' | 'thisMonth' | 'future' | 'paid';

/** Maps a tab to ListPayables filters. Computed at call time so "today" stays correct past midnight. */
function tabToParams(tab: TabKey): Pick<PayablesParams, 'paid' | 'dueFrom' | 'dueTo'> {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  switch (tab) {
    case 'overdue': // não pagas, vencimento já passou (até ontem)
      return { paid: false, dueTo: toDateOnly(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1)) };
    case 'thisMonth': // não pagas, a vencer ainda neste mês (hoje → último dia do mês)
      return {
        paid: false,
        dueFrom: toDateOnly(today),
        dueTo: toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 0)),
      };
    case 'future': // não pagas, vencem em meses futuros (a partir do 1º dia do próximo mês)
      return { paid: false, dueFrom: toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 1)) };
    case 'paid': // parcelas liquidadas (qualquer data)
      return { paid: true };
    case 'all':
    default:
      return {};
  }
}

const TABS: TabKey[] = ['all', 'overdue', 'thisMonth', 'future', 'paid'];

function PayableStatusBadge({ s }: { s: PayableStatus }) {
  const { t } = useTranslation();
  const cls: Record<PayableStatus, string> = {
    PendingApproval: 'badge-pendente',
    Approved: 'badge-aprovado',
    Rejected: 'badge-rejeitado',
    Cancelled: 'badge-rejeitado',
    Settled: 'badge-pago',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`payables.status.${s}`)}</span>;
}

function InstallmentStatusBadge({ s }: { s: InstallmentStatus }) {
  const { t } = useTranslation();
  const cls: Record<InstallmentStatus, string> = {
    Open: 'badge-pendente',
    PartiallyPaid: 'badge-aprovado',
    Paid: 'badge-pago',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`installments.status.${s}`)}</span>;
}

/** Maps id→name of the company's cost centers + expense categories (to show names in lists). */
function useNames(companyId: string) {
  const centros = useCostCenters(companyId).data ?? [];
  const categorias = useExpenseCategories(true).data ?? [];
  return {
    centros,
    centroNome: useMemo(() => Object.fromEntries(centros.map((c) => [c.id, c.name])), [centros]),
    categoriaNome: useMemo(() => Object.fromEntries(categorias.map((c) => [c.id, c.name])), [categorias]),
  };
}

/* ============ LIST ============ */
export function ContasPagarPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [tab, setTab] = useState<TabKey>('all');
  const { centroNome, categoriaNome } = useNames(empresaId);
  const filters = useMemo(() => tabToParams(tab), [tab]);
  const list = usePayables({ companyId: empresaId, page, ...filters }, { enabled: !!empresaId });
  const itens = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  useEffect(() => setPage(1), [empresaId]);
  // Synchronous reset: clear the page alongside the tab switch to avoid a request with an out-of-range page.
  const changeTab = (k: TabKey) => {
    setTab(k);
    setPage(1);
  };

  return (
    <>
      <PageHeader title={t('payables.title')} sub={t('payables.subtitle')}>
        <EmpresaPicker />
        <Button
          variant='brand'
          icon={<Plus />}
          disabled={!empresa}
          style={{ alignSelf: 'flex-end' }}
          onClick={() => nav('/app/contas-a-pagar/novo')}
        >
          {t('payables.new')}
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Wallet />}
          title={t('payables.empty.noCompanyTitle')}
          sub={t('payables.empty.noCompanySub')}
        />
      ) : (
        <>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
            {TABS.map((k) => (
              <Button key={k} variant={tab === k ? 'brand' : 'default'} onClick={() => changeTab(k)}>
                {t(`payables.tabs.${k}`)}
              </Button>
            ))}
          </div>
          {itens.length === 0 ? (
            <EmptyState
              icon={<Wallet />}
              title={t('payables.empty.noItemsTitle')}
              sub={t('payables.empty.noItemsSub')}
            />
          ) : (
            <Panel title={t('payables.count', { count: list.data?.total ?? itens.length })}>
              <table>
                <thead>
                  <tr>
                    <th>{t('payables.columns.dueDate')}</th>
                    <th>{t('payables.columns.description')}</th>
                    <th>{t('payables.columns.vendor')}</th>
                    <th>{t('payables.columns.installment')}</th>
                    <th>{t('payables.columns.costCenter')}</th>
                    <th>{t('payables.columns.category')}</th>
                    <th style={{ textAlign: 'right' }}>{t('payables.columns.amount')}</th>
                    <th>{t('payables.columns.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {itens.map((l) => (
                    <tr
                      key={l.installmentId}
                      className='row-click'
                      onClick={() => nav(`/app/contas-a-pagar/${l.payableId}`)}
                    >
                      <td className='t-mono'>{fmtData(l.dueDate)}</td>
                      <td>{l.description}</td>
                      <td>{l.counterpartyName || '—'}</td>
                      <td className='t-mono'>
                        {l.sequence}/{l.installmentCount}
                      </td>
                      <td>{centroNome[l.costCenterId] ?? '—'}</td>
                      <td>{(l.categoryId ? categoriaNome[l.categoryId] : null) ?? '—'}</td>
                      <td className='amount-cell' style={{ textAlign: 'right' }}>
                        {brl(l.amount)}
                      </td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
                          <PayableStatusBadge s={l.payableStatus} />
                          {l.payableStatus === 'Approved' && <InstallmentStatusBadge s={l.installmentStatus} />}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <Pager page={page} totalPages={totalPages} onPage={setPage} />
            </Panel>
          )}
        </>
      )}
    </>
  );
}

/* ============ Vendor picker (Combobox + inline create) ============ */
function VendorPicker({
  companyId,
  value,
  onChange,
  invalid,
  prefill,
  notice,
}: {
  companyId: string;
  value: string | null;
  onChange: (id: string | null) => void;
  invalid?: boolean;
  /** Pre-fill for the inline create form (e.g. issuer data from the NF when no vendor matched). */
  prefill?: Partial<VendorValues>;
  notice?: ReactNode;
}) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [drawerOpen, setDrawerOpen] = useState(false);

  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(id);
  }, [search]);

  const vendors = useVendors(companyId, debouncedSearch);
  const items = (vendors.data?.items ?? []).map((p) => ({
    id: p.id,
    label: p.name,
    sub: p.tradeName ?? p.document,
  }));

  return (
    <>
      <div style={{ display: 'flex', gap: 8, alignItems: 'flex-start' }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <Combobox
            value={value}
            onChange={onChange}
            items={items}
            invalid={invalid}
            loading={vendors.isLoading}
            placeholder={t('transactions.counterparty.selectVendor')}
            searchPlaceholder={t('transactions.counterparty.searchVendor')}
            emptyText={t('transactions.counterparty.emptyVendor')}
            onSearch={setSearch}
            footer={
              <Button type='button' variant='ghost' icon={<Plus />} onClick={() => setDrawerOpen(true)}>
                {t('transactions.counterparty.newVendor')}
              </Button>
            }
          />
        </div>
        {/* When the NF issuer had no matching vendor, offer a prominent prefilled "register vendor from NF". */}
        {prefill && (
          <Button type='button' variant='brand' icon={<Plus />} onClick={() => setDrawerOpen(true)}>
            {t('payables.intake.createVendorFromNf')}
          </Button>
        )}
      </div>
      <Drawer
        open={drawerOpen}
        title={t('transactions.counterparty.newVendorTitle')}
        onClose={() => setDrawerOpen(false)}
      >
        {drawerOpen && (
          <VendorForm
            companyId={companyId}
            prefill={prefill}
            notice={notice}
            onSaved={(v) => {
              onChange(v.id);
              setDrawerOpen(false);
            }}
            onCancel={() => setDrawerOpen(false)}
          />
        )}
      </Drawer>
    </>
  );
}

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
  // Presigned-upload state: key + metadata stored after the blob PUT succeeds.
  const [fiscalUploadKey, setFiscalUploadKey] = useState<string | null>(null);
  const [fiscalUploadMeta, setFiscalUploadMeta] = useState<{
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
      // Persist the fiscal document of record (NF) using the key from the presigned upload, then any generic attachment.
      if (fiscalUploadKey && fiscalUploadMeta) {
        await uploadFiscal.mutateAsync({
          id: p.id,
          key: fiscalUploadKey,
          fileName: fiscalUploadMeta.fileName,
          contentType: fiscalUploadMeta.contentType,
          sizeBytes: fiscalUploadMeta.sizeBytes,
        });
      }
      if (!editing && file) await upload.mutateAsync({ id: p.id, file });
      nav(`/app/contas-a-pagar/${p.id}`);
    } catch {
      // Errors surface via the mutation hooks' `error` state (see `erro`).
    }
  };

  const backTo = editing && initial ? `/app/contas-a-pagar/${initial.id}` : '/app/contas-a-pagar';

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
          {!revealed ? (
            <div
              data-testid='intake-cards'
              style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, maxWidth: 720, margin: '0 auto' }}
            >
              {/* Card: Preencher com NF */}
              <div className='card-form' style={{ maxWidth: 'none', textAlign: 'center' }}>
                <div className='section-title' style={{ marginTop: 0 }}>
                  <Receipt
                    style={{ width: 18, height: 18, color: 'var(--brand)', display: 'inline', marginRight: 6 }}
                  />
                  {t('payables.intake.withNf')}
                </div>
                <p className='help' style={{ marginBottom: 12 }}>
                  {t('payables.intake.withNfHint')}
                </p>
                <FiscalXmlReader
                  companyId={empresaId}
                  file={fiscalFile}
                  onFile={setFiscalFile}
                  onApply={applyParsed}
                  onClear={clearXmlFields}
                  onUploadKey={(key, meta) => {
                    setFiscalUploadKey(key);
                    setFiscalUploadMeta(meta);
                  }}
                  variant='hero'
                  embedded
                />
              </div>
              {/* Card: Preencher manualmente */}
              <button
                data-testid='manual-card'
                type='button'
                className='card-form'
                style={{ maxWidth: 'none', textAlign: 'center', cursor: 'pointer', border: '2px dashed var(--border)' }}
                onClick={() => setManualMode(true)}
              >
                <div className='section-title' style={{ marginTop: 0 }}>
                  <Pencil
                    style={{
                      width: 18,
                      height: 18,
                      color: 'var(--muted-foreground)',
                      display: 'inline',
                      marginRight: 6,
                    }}
                  />
                  {t('payables.intake.manual')}
                </div>
                <p className='help'>{t('payables.intake.manualHint')}</p>
              </button>
            </div>
          ) : (
            <FiscalXmlReader
              companyId={empresaId}
              file={fiscalFile}
              onFile={setFiscalFile}
              onApply={applyParsed}
              onClear={clearXmlFields}
              onUploadKey={(key, meta) => {
                setFiscalUploadKey(key);
                setFiscalUploadMeta(meta);
              }}
              variant='band'
            />
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
                          <td className='t-mono'>{fmtData(p.dueDate)}</td>
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
                          <td className='t-mono'>{fmtData(p.dueDate)}</td>
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

/* ============ FISCAL XML/PDF READER (NF card / band) ============ */
/**
 * NF reader: upload an XML (NF-e/NFS-e) or PDF.
 * Flow: request presigned URL → PUT file to it → call parse by key → apply to form.
 * On Storage.PresignNotSupported (local dev), shows a non-blocking notice and lets the user proceed manually.
 */
function FiscalXmlReader({
  companyId,
  file,
  onFile,
  onApply,
  onClear,
  onUploadKey,
  variant,
  embedded,
}: {
  companyId: string;
  file: File | null;
  onFile: (f: File | null) => void;
  onApply: (p: ParsedFiscalDocument) => void;
  onClear: () => void;
  /** Called when the file has been uploaded to blob storage and a key is available. */
  onUploadKey: (key: string, meta: { fileName: string; contentType: string; sizeBytes: number }) => void;
  variant: 'hero' | 'band';
  /** When true, suppresses the outer card wrapper (the parent card already provides it). */
  embedded?: boolean;
}) {
  const { t } = useTranslation();
  const requestUrl = useRequestFiscalUploadUrl();
  const parse = useParseFiscalDocument();
  const [parsed, setParsed] = useState<ParsedFiscalDocument | null>(null);
  const [uploadUnavailable, setUploadUnavailable] = useState(false);
  const [uploadErr, setUploadErr] = useState<string | null>(null);
  const hero = variant === 'hero';

  // Selecting a file: request upload URL → PUT to it → parse by key → apply.
  const onSelect = (f: File | null) => {
    setParsed(null);
    parse.reset();
    requestUrl.reset();
    setUploadErr(null);
    setUploadUnavailable(false);
    onClear();
    onFile(f);
    if (!f) return;

    requestUrl.mutate(
      { fileName: f.name },
      {
        onSuccess: async (target) => {
          try {
            await uploadToPresignedUrl(target.uploadUrl, f);
          } catch (e) {
            setUploadErr(e instanceof Error ? e.message : String(e));
            return;
          }
          onUploadKey(target.key, { fileName: f.name, contentType: f.type, sizeBytes: f.size });
          parse.mutate(
            { companyId, key: target.key, fileName: f.name },
            {
              onSuccess: (p) => {
                setParsed(p);
                onApply(p);
              },
            },
          );
        },
        onError: (err) => {
          // Storage.PresignNotSupported → non-blocking notice, let user proceed manually.
          const msg = err instanceof Error ? err.message : String(err);
          if (msg.includes('PresignNotSupported')) {
            setUploadUnavailable(true);
          } else {
            setUploadErr(msg);
          }
        },
      },
    );
  };

  const clearAll = () => {
    setParsed(null);
    parse.reset();
    requestUrl.reset();
    setUploadErr(null);
    setUploadUnavailable(false);
    onFile(null);
    onClear();
  };

  const isPending = requestUrl.isPending || parse.isPending;

  const inner = (
    <>
      {!embedded && (
        <div className='section-title' style={{ marginTop: 0, display: 'flex', alignItems: 'center', gap: 8 }}>
          {!hero && <Receipt style={{ width: 16, height: 16, color: 'var(--brand)' }} />}
          {hero ? t('payables.fiscalReader.title') : t('payables.fiscalReader.bandTitle')}
          {!hero && parsed && <span className='tag-readonly'>{t('payables.fiscalReader.readOnlyBadge')}</span>}
        </div>
      )}
      {hero && !embedded && <p className='fiscal-hero-emphasis'>{t('payables.fiscalReader.heroEmphasis')}</p>}
      <Field label={t('payables.fiscalReader.selectLabel')} help={t('payables.fiscalReader.selectHelp')}>
        <FilePicker
          accept='.xml,application/xml,text/xml,.pdf,application/pdf'
          fileName={file?.name}
          placeholder={hero ? t('payables.fiscalReader.dropHint') : t('common.noFileSelected')}
          onSelect={onSelect}
          large={hero && !embedded}
        />
      </Field>

      {uploadUnavailable && (
        <div className='help' style={{ marginTop: 10 }}>
          {t('payables.fiscalReader.uploadUnavailable')}
        </div>
      )}
      {isPending && (
        <div className='help' style={{ marginTop: 10 }}>
          {t('payables.fiscalReader.parsing')}
        </div>
      )}
      {(uploadErr ?? (parse.error != null ? getErrorMessage(parse.error) : null)) && (
        <div className='create-err' style={{ marginTop: 10 }}>
          {uploadErr ?? getErrorMessage(parse.error)}
        </div>
      )}

      {parsed && (
        <>
          <div className='help' style={{ margin: '12px 0 8px' }}>
            {t('payables.fiscalReader.applied')}
          </div>
          <div className='edit-grid'>
            <FiscalField
              label={t('payables.fiscalReader.format')}
              value={t(`payables.fiscalReader.formats.${parsed.format}`)}
            />
            <FiscalField label={t('payables.fields.documentNumber')} value={parsed.documentNumber ?? '—'} />
            <FiscalField
              label={t('payables.fields.issueDate')}
              value={parsed.issueDate ? fmtData(parsed.issueDate.slice(0, 10)) : '—'}
            />
            {parsed.rpsNumber && <FiscalField label={t('payables.fiscalReader.rpsNumber')} value={parsed.rpsNumber} />}
            {parsed.rpsSeries && <FiscalField label={t('payables.fiscalReader.rpsSeries')} value={parsed.rpsSeries} />}
            <FiscalField
              label={t('payables.fields.totalAmount')}
              value={parsed.totalAmount != null ? brl(parsed.totalAmount) : '—'}
            />
            {parsed.cnaeCode && <FiscalField label={t('payables.fiscalReader.cnae')} value={parsed.cnaeCode} />}
            {parsed.installments.length > 0 && (
              <FiscalField
                label={t('payables.fiscalReader.installmentsCount')}
                value={String(parsed.installments.length)}
              />
            )}
            {/* Fiscal summary (NF-e): CFOP/NCM counts + total taxes */}
            {parsed.fiscal && (
              <FiscalField
                full
                label={t('payables.fiscalReader.taxes')}
                value={[
                  parsed.fiscal.cfops.length > 0 && `CFOPs: ${parsed.fiscal.cfops.join(', ')}`,
                  parsed.fiscal.ncms.length > 0 && `NCMs: ${parsed.fiscal.ncms.length}`,
                  parsed.fiscal.totalTaxes != null &&
                    `${t('payables.fiscalReader.totalTaxes')}: ${brl(parsed.fiscal.totalTaxes)}`,
                ]
                  .filter(Boolean)
                  .join(' · ')}
              />
            )}
            {/* Emitente e CNPJ do emitente lado a lado (linha própria de 2 colunas). */}
            <div style={{ gridColumn: '1 / -1', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px 16px' }}>
              <FiscalField label={t('payables.fiscalReader.issuer')} value={parsed.issuerName ?? '—'} />
              <FiscalField
                label={t('payables.fiscalReader.issuerDocument')}
                value={parsed.issuerDocument ? fmtDoc(parsed.issuerDocument) : '—'}
              />
            </div>
            <FiscalField
              full
              label={t('payables.fiscalReader.vendorMatch')}
              value={parsed.matchedVendorName ?? t('payables.fiscalReader.vendorNoMatch')}
            />
            {parsed.serviceDescription && (
              <FiscalField
                full
                label={t(
                  parsed.format === 'NfeXml'
                    ? 'payables.fiscalReader.productDescription'
                    : 'payables.fiscalReader.serviceDescription',
                )}
                value={parsed.serviceDescription}
              />
            )}
          </div>
          <Button
            type='button'
            onClick={clearAll}
            style={{ marginTop: 14, background: 'var(--surface)', color: 'var(--muted-foreground)' }}
          >
            {t('payables.fiscalReader.clear')}
          </Button>
        </>
      )}
    </>
  );

  if (embedded) return inner;

  return (
    <div className={hero ? 'card-form fiscal-hero' : 'card-form fiscal-band'} style={{ maxWidth: 'none' }}>
      {inner}
    </div>
  );
}

/** Read-only field for the NF preview — same Label + input.input look as the form. */
function FiscalField({ label, value, full }: { label: string; value: string; full?: boolean }) {
  return (
    <div style={full ? { gridColumn: '1 / -1' } : undefined}>
      <Field label={label}>
        <Input value={value} readOnly />
      </Field>
    </div>
  );
}

/** Styled file input: upload icon + the selected file name, or a placeholder when empty. */
export function FilePicker({
  accept,
  fileName,
  placeholder,
  onSelect,
  large,
}: {
  accept: string;
  fileName?: string | null;
  placeholder: string;
  onSelect: (f: File | null) => void;
  large?: boolean;
}) {
  return (
    <label className={large ? 'file-picker file-picker-lg' : 'file-picker'}>
      <Upload />
      <span className={fileName ? 'file-name' : undefined}>{fileName ?? placeholder}</span>
      <input
        type='file'
        accept={accept}
        style={{ display: 'none' }}
        onChange={(e) => {
          onSelect(e.target.files?.[0] ?? null);
          // Reset the input so re-selecting the SAME file fires onChange again (e.g. load → clear → reload same NF).
          e.target.value = '';
        }}
      />
    </label>
  );
}

export function NovaContaPagarPage() {
  return <PayableForm />;
}

export function EditarContaPagarPage() {
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

/* ============ Attachment download (authed fetch → blob) ============ */
/**
 * The download endpoint requires a Bearer token, which a plain `<a href>` cannot
 * send. So we fetch the file with the Authorization header, turn the response into
 * an object-URL blob, and trigger a programmatic download.
 */
async function downloadAttachment(payableId: string, att: PayableAttachment): Promise<void> {
  const url = `${getApiBaseUrl()}/payables/${payableId}/attachments/${att.id}`;
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = att.fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(objectUrl);
}

/**
 * Downloads a payment proof. Same authed-fetch→blob approach as the attachment
 * download: a plain `<a href>` can't send the Bearer token, so we fetch with the
 * Authorization header, turn the response into an object-URL blob, and trigger a
 * programmatic download.
 */
async function downloadPaymentProof(payableId: string, payment: Payment): Promise<void> {
  const url = api.downloadPaymentProofUrl(payableId, payment.id);
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = `comprovante-${payment.id}`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(objectUrl);
}

const PAYMENT_METHODS: PaymentMethod[] = ['Pix', 'Ted', 'Boleto', 'Cash', 'DirectDebit', 'Other'];

/* ============ Baixa (register payment) form ============ */
function PaymentForm({
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

  const remaining = Math.round((installment.amount - installment.paidAmount) * 100) / 100;

  const { control, handleSubmit, formState } = useForm<PaymentValues>({
    resolver: resolver(paymentSchema),
    mode: 'onChange',
    defaultValues: {
      paymentDate: new Date().toISOString().slice(0, 10),
      amount: String(remaining > 0 ? remaining : installment.amount),
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

/* ============ Per-installment payment history + actions ============ */
function PaymentHistory({ payable, installment }: { payable: Payable; installment: Installment }) {
  const { t } = useTranslation();
  const reverse = useReversePayment();
  const [toReverse, setToReverse] = useState<Payment | null>(null);
  const [reverseReason, setReverseReason] = useState('');
  const [downloadErr, setDownloadErr] = useState<string | null>(null);

  const payments = [...installment.payments].sort((a, b) => a.paymentDate.localeCompare(b.paymentDate));
  if (payments.length === 0) return null;

  return (
    <>
      <table>
        <thead>
          <tr>
            <th>{t('payments.columns.date')}</th>
            <th style={{ textAlign: 'right' }}>{t('payments.columns.amount')}</th>
            <th>{t('payments.columns.method')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {payments.map((pay) => (
            <tr key={pay.id}>
              <td className='t-mono'>{fmtData(pay.paymentDate)}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(pay.amount)}
              </td>
              <td>{t(`payments.methods.${pay.method}`)}</td>
              <td className='td-action'>
                <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center' }}>
                  {pay.isReversed ? (
                    <span className='badge-status badge-rejeitado'>{t('payments.reversed')}</span>
                  ) : (
                    <>
                      {pay.hasProof && (
                        <Button
                          icon={<Download />}
                          onClick={() => {
                            setDownloadErr(null);
                            downloadPaymentProof(payable.id, pay).catch(() =>
                              setDownloadErr(t('payments.downloadError')),
                            );
                          }}
                        >
                          {t('payments.downloadProof')}
                        </Button>
                      )}
                      <Button
                        icon={<Undo2 />}
                        onClick={() => {
                          setReverseReason('');
                          setToReverse(pay);
                        }}
                      >
                        {t('payments.reverse')}
                      </Button>
                    </>
                  )}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {downloadErr && <p className='create-err'>{downloadErr}</p>}

      {toReverse && (
        <ConfirmDialog
          title={t('payments.reverseDialog.title')}
          danger
          busy={reverse.isPending}
          confirmLabel={t('payments.reverseDialog.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payments.reverseDialog.message')}</div>
              <Input
                autoFocus
                value={reverseReason}
                placeholder={t('payments.reverseDialog.placeholder')}
                onChange={(e) => setReverseReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            reverse.mutate(
              { payableId: payable.id, paymentId: toReverse.id, reason: reverseReason.trim() },
              { onSuccess: () => setToReverse(null) },
            )
          }
          onCancel={() => setToReverse(null)}
        />
      )}
    </>
  );
}

/* ============ DETAIL ============ */
export function ContaPagarDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const { t } = useTranslation();
  const query = usePayable(id);
  const p = query.data ?? null;

  const centros = useCostCenters(p?.companyId ?? '').data ?? [];
  const categorias = useExpenseCategories(true).data ?? [];
  const accountingAccounts = useChartAccounts({ chartType: 'Accounting' }).data ?? [];
  const financialAccounts = useChartAccounts({ chartType: 'Financial' }).data ?? [];

  const approve = useApprovePayable();
  const reject = useRejectPayable();
  const cancel = useCancelPayable();
  const deleteAttachment = useDeletePayableAttachment();

  const [rejectOpen, setRejectOpen] = useState(false);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [cancelReason, setCancelReason] = useState('');
  const [attToDelete, setAttToDelete] = useState<PayableAttachment | null>(null);
  const [downloadErr, setDownloadErr] = useState<string | null>(null);
  const [baixaInstallment, setBaixaInstallment] = useState<Installment | null>(null);

  if (query.isLoading) return <div className='empty-state'>{t('payables.loading')}</div>;
  if (!p)
    return (
      <EmptyState icon={<Wallet />} title={t('payables.empty.notFoundTitle')} sub={t('payables.empty.notFoundSub')} />
    );

  const centroNome = centros.find((c) => c.id === p.costCenterId)?.name ?? '—';
  const categoriaNome = categorias.find((c) => c.id === p.categoryId)?.name ?? '—';
  const accountLabel = (accs: typeof accountingAccounts, id: string | null) => {
    if (!id) return t('payables.notClassified');
    const a = accs.find((x) => x.id === id);
    return a ? `${a.code} — ${a.name}` : t('payables.notClassified');
  };
  const editable = p.status === 'PendingApproval' || p.status === 'Approved';

  return (
    <>
      <button className='backlink' onClick={() => nav('/app/contas-a-pagar')}>
        <ChevronLeft /> {t('payables.back')}
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{p.description}</h1>
          <div className='detail-sub' style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <span>
              {t('payables.sub')} · {p.counterpartyName || '—'}
            </span>
            <span className={`badge-status ${p.isClassified ? 'badge-pago' : 'badge-pendente'}`}>
              {p.isClassified ? t('payables.classified') : t('payables.unclassified')}
            </span>
          </div>
        </div>
        <div className='detail-actions'>
          {p.status === 'PendingApproval' && (
            <Button className='btn-approve' icon={<Check />} onClick={() => approve.mutate({ id: p.id })}>
              {t('payables.actions.approve')}
            </Button>
          )}
          {p.status === 'PendingApproval' && (
            <Button icon={<X />} onClick={() => setRejectOpen(true)}>
              {t('payables.actions.reject')}
            </Button>
          )}
          {editable && (
            <Button icon={<Pencil />} onClick={() => nav(`/app/contas-a-pagar/${p.id}/editar`)}>
              {t('payables.actions.edit')}
            </Button>
          )}
          {editable && (
            <Button icon={<X />} onClick={() => setCancelOpen(true)}>
              {t('payables.actions.cancel')}
            </Button>
          )}
        </div>
      </div>

      <div className='info-grid'>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.totalAmount')}</div>
          <div className='info-value'>{brl(p.totalAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.status')}</div>
          <div style={{ marginTop: 4 }}>
            <PayableStatusBadge s={p.status} />
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.costCenter')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {centroNome}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.category')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {categoriaNome}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.accountingAccount')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {accountLabel(accountingAccounts, p.accountingAccountId)}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.financialAccount')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {accountLabel(financialAccounts, p.financialAccountId)}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.documentNumber')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {p.documentNumber || '—'}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.issueDate')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {p.issueDate ? fmtData(p.issueDate) : '—'}
          </div>
        </div>
      </div>

      {p.rejectionReason && (
        <p className='create-err'>{t('payables.rejectionReason', { reason: p.rejectionReason })}</p>
      )}
      {p.cancelReason && <p className='create-err'>{t('payables.cancelReason', { reason: p.cancelReason })}</p>}

      {(p.status === 'Approved' || p.status === 'Settled') && p.approvedBy && p.approvedAt && (
        <p className='detail-sub'>
          {t('payables.approvedBy', { user: p.approvedBy, date: fmtDateTime(p.approvedAt) })}
        </p>
      )}
      {p.status === 'Rejected' && p.rejectedBy && p.rejectedAt && (
        <p className='detail-sub'>
          {t('payables.rejectedBy', { user: p.rejectedBy, date: fmtDateTime(p.rejectedAt) })}
        </p>
      )}

      <Panel title={t('payables.installmentsTitle')}>
        <table>
          <thead>
            <tr>
              <th>{t('installments.columns.sequence')}</th>
              <th>{t('installments.columns.dueDate')}</th>
              <th style={{ textAlign: 'right' }}>{t('installments.columns.amount')}</th>
              <th>{t('installments.columns.status')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {[...p.installments]
              .sort((a, b) => a.sequence - b.sequence)
              .map((inst) => (
                <tr key={inst.id}>
                  <td className='t-mono'>
                    {inst.sequence}/{p.installments.length}
                  </td>
                  <td className='t-mono'>{fmtData(inst.dueDate)}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(inst.amount)}
                  </td>
                  <td>
                    <InstallmentStatusBadge s={inst.status} />
                  </td>
                  <td className='td-action'>
                    {p.status === 'Approved' && inst.status !== 'Paid' && (
                      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                        <Button icon={<Receipt />} onClick={() => setBaixaInstallment(inst)}>
                          {t('payments.register')}
                        </Button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
          </tbody>
        </table>
      </Panel>

      {[...p.installments]
        .sort((a, b) => a.sequence - b.sequence)
        .filter((inst) => inst.payments.length > 0)
        .map((inst) => (
          <Panel
            key={inst.id}
            title={`${t('payments.title')} — ${t('installments.label')} ${inst.sequence}/${p.installments.length}`}
          >
            <PaymentHistory payable={p} installment={inst} />
          </Panel>
        ))}

      <Panel title={t('attachments.title')}>
        {p.attachments.length === 0 ? (
          <div className='empty-state' style={{ padding: 24 }}>
            <div className='s'>{t('attachments.empty')}</div>
          </div>
        ) : (
          <table>
            <tbody>
              {p.attachments.map((att) => (
                <tr key={att.id}>
                  <td>
                    <Paperclip style={{ width: 14, height: 14, verticalAlign: 'middle', marginRight: 6 }} />
                    {att.fileName}
                  </td>
                  <td className='td-action'>
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      <Button
                        icon={<Download />}
                        onClick={() => {
                          setDownloadErr(null);
                          downloadAttachment(p.id, att).catch(() => setDownloadErr(t('attachments.downloadError')));
                        }}
                      >
                        {t('attachments.download')}
                      </Button>
                      <Button icon={<Trash2 />} onClick={() => setAttToDelete(att)}>
                        {t('attachments.remove')}
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {downloadErr && <p className='create-err'>{downloadErr}</p>}
      </Panel>

      {rejectOpen && (
        <ConfirmDialog
          title={t('payables.reject.title')}
          danger
          busy={reject.isPending}
          confirmLabel={t('payables.reject.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payables.reject.message')}</div>
              <Input
                autoFocus
                value={rejectReason}
                placeholder={t('payables.reject.placeholder')}
                onChange={(e) => setRejectReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            reject.mutate({ id: p.id, reason: rejectReason.trim() }, { onSuccess: () => setRejectOpen(false) })
          }
          onCancel={() => setRejectOpen(false)}
        />
      )}

      {cancelOpen && (
        <ConfirmDialog
          title={t('payables.cancel.title')}
          danger
          busy={cancel.isPending}
          confirmLabel={t('payables.cancel.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payables.cancel.message')}</div>
              <Input
                autoFocus
                value={cancelReason}
                placeholder={t('payables.cancel.placeholder')}
                onChange={(e) => setCancelReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            cancel.mutate({ id: p.id, reason: cancelReason.trim() }, { onSuccess: () => setCancelOpen(false) })
          }
          onCancel={() => setCancelOpen(false)}
        />
      )}

      <Drawer
        open={!!baixaInstallment}
        title={t('payments.registerTitle')}
        sub={
          baixaInstallment
            ? `${t('installments.label')} ${baixaInstallment.sequence}/${p.installments.length}`
            : undefined
        }
        onClose={() => setBaixaInstallment(null)}
      >
        {baixaInstallment && (
          <PaymentForm
            payable={p}
            installment={baixaInstallment}
            onDone={() => setBaixaInstallment(null)}
            onCancel={() => setBaixaInstallment(null)}
          />
        )}
      </Drawer>

      {attToDelete && (
        <ConfirmDialog
          title={t('attachments.deleteTitle')}
          danger
          busy={deleteAttachment.isPending}
          confirmLabel={t('attachments.remove')}
          message={t('attachments.deleteMessage', { name: attToDelete.fileName })}
          onConfirm={() =>
            deleteAttachment.mutate(
              { id: p.id, attachmentId: attToDelete.id },
              { onSuccess: () => setAttToDelete(null) },
            )
          }
          onCancel={() => setAttToDelete(null)}
        />
      )}
    </>
  );
}
