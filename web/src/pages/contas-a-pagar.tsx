import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Plus, ChevronLeft, Check, X, Wallet, Pencil, Download, Trash2, Paperclip, Receipt, Undo2 } from 'lucide-react';
import {
  api,
  auth,
  type Payable,
  type PayableInput,
  type PayableStatus,
  type Installment,
  type InstallmentStatus,
  type PayableAttachment,
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
} from './schemas';
import { useApp } from '../store';
import {
  useApprovePayable,
  useBankAccounts,
  useCancelPayable,
  useCostCenters,
  useCreatePayable,
  useDeletePayableAttachment,
  useExpenseCategories,
  usePayable,
  usePayables,
  useRegisterPayment,
  useRejectPayable,
  useReversePayment,
  useUpdatePayable,
  useUploadPayableAttachment,
  useUploadPaymentProof,
  useVendors,
} from '../queries';
import { brl } from '../lib';
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
import { VendorForm } from './registry-forms';

const fmtData = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');
/** Formats an ISO date-time (e.g. approvedAt/rejectedAt) as a pt-BR date. */
const fmtDateTime = (iso: string) => new Date(iso).toLocaleDateString('pt-BR');

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
  const { centroNome, categoriaNome } = useNames(empresaId);
  const list = usePayables({ companyId: empresaId, page }, { enabled: !!empresaId });
  const itens = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  useEffect(() => setPage(1), [empresaId]);

  return (
    <>
      <PageHeader title={t('payables.title')} sub={t('payables.subtitle')}>
        <EmpresaPicker />
        <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => nav('/app/contas-a-pagar/novo')}>
          {t('payables.new')}
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Wallet />}
          title={t('payables.empty.noCompanyTitle')}
          sub={t('payables.empty.noCompanySub')}
        />
      ) : itens.length === 0 ? (
        <EmptyState icon={<Wallet />} title={t('payables.empty.noItemsTitle')} sub={t('payables.empty.noItemsSub')} />
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
  );
}

/* ============ Vendor picker (Combobox + inline create) ============ */
function VendorPicker({
  companyId,
  value,
  onChange,
  invalid,
}: {
  companyId: string;
  value: string | null;
  onChange: (id: string | null) => void;
  invalid?: boolean;
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
      <Drawer
        open={drawerOpen}
        title={t('transactions.counterparty.newVendorTitle')}
        onClose={() => setDrawerOpen(false)}
      >
        {drawerOpen && (
          <VendorForm
            companyId={companyId}
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
  const categorias = useExpenseCategories().data ?? [];
  const [file, setFile] = useState<File | null>(null);

  const editing = !!initial;
  // Derive form mode/values from an existing payable (installments → parcelado when >1).
  const fromInitial = (): PayableValues => {
    if (!initial) {
      return {
        vendorId: '',
        costCenterId: '',
        categoryId: null,
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

  const { control, handleSubmit, watch, formState } = useForm<PayableValues>({
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
  const saving = create.isPending || update.isPending || upload.isPending;
  const erro = create.error ? getErrorMessage(create.error) : update.error ? getErrorMessage(update.error) : null;

  const buildBody = (v: PayableValues): Omit<PayableInput, 'origin'> => {
    const base = {
      companyId: empresaId,
      vendorId: v.vendorId,
      costCenterId: v.costCenterId,
      categoryId: v.categoryId || null,
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

  const onValid = (v: PayableValues) => {
    const body = buildBody(v);
    if (editing && initial) {
      update.mutate({ id: initial.id, body }, { onSuccess: (p) => nav(`/app/contas-a-pagar/${p.id}`) });
    } else {
      create.mutate(body, {
        onSuccess: (p) => {
          if (file) {
            upload.mutate({ id: p.id, file }, { onSuccess: () => nav(`/app/contas-a-pagar/${p.id}`) });
          } else {
            nav(`/app/contas-a-pagar/${p.id}`);
          }
        },
      });
    }
  };

  const backTo = editing && initial ? `/app/contas-a-pagar/${initial.id}` : '/app/contas-a-pagar';

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
        <form className='card-form' onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
          <div className='edit-grid'>
            <Field label={t('payables.fields.vendor')}>
              <Controller
                control={control}
                name='vendorId'
                render={({ field }) => (
                  <VendorPicker
                    companyId={empresaId}
                    value={field.value || null}
                    onChange={(id) => field.onChange(id ?? '')}
                    invalid={!!formState.errors.vendorId}
                  />
                )}
              />
              {fieldError(formState.errors.vendorId?.message) && (
                <div className='field-err'>{fieldError(formState.errors.vendorId?.message)}</div>
              )}
            </Field>
            <Field label={t('payables.fields.category')}>
              <Controller
                control={control}
                name='categoryId'
                render={({ field }) => (
                  <Combobox
                    value={field.value ?? null}
                    onChange={(id) => field.onChange(id)}
                    items={categorias.map((c) => ({ id: c.id, label: c.name }))}
                    placeholder={t('payables.fields.categoryNone')}
                  />
                )}
              />
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
            <Field label={t('payables.fields.documentNumber')}>
              <Controller
                control={control}
                name='documentNumber'
                render={({ field }) => <Input {...field} placeholder={t('payables.placeholders.documentNumber')} />}
              />
            </Field>
            <Field label={t('payables.fields.issueDate')}>
              <Controller control={control} name='issueDate' render={({ field }) => <Input type='date' {...field} />} />
            </Field>
          </div>

          <div style={{ marginTop: 14 }}>
            <Field label={t('payables.fields.description')}>
              <Controller
                control={control}
                name='description'
                render={({ field }) => <Input {...field} placeholder={t('payables.placeholders.description')} />}
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
                      onClick={() => field.onChange('single')}
                    >
                      {t('payables.mode.single')}
                    </Button>
                    <Button
                      type='button'
                      variant={field.value === 'installments' ? 'brand' : 'default'}
                      onClick={() => field.onChange('installments')}
                    >
                      {t('payables.mode.installments')}
                    </Button>
                  </div>
                )}
              />
            </Field>
          </div>

          <div className='edit-grid' style={{ marginTop: 14 }}>
            <Field label={mode === 'single' ? t('payables.fields.amount') : t('payables.fields.totalAmount')}>
              <Controller
                control={control}
                name='totalAmount'
                render={({ field }) => (
                  <Input {...field} inputMode='numeric' placeholder={t('payables.placeholders.amount')} />
                )}
              />
              {fieldError(formState.errors.totalAmount?.message) && (
                <div className='field-err'>{fieldError(formState.errors.totalAmount?.message)}</div>
              )}
            </Field>
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
            {mode === 'installments' && (
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

          {mode === 'installments' && preview.length > 0 && (
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

          {!editing && (
            <div style={{ marginTop: 14 }}>
              <Field label={t('payables.upload.label')} help={t('payables.upload.help')}>
                <Input
                  type='file'
                  accept='application/pdf,image/png,image/jpeg'
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
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
    </>
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
  const editable = p.status === 'PendingApproval' || p.status === 'Approved';

  return (
    <>
      <button className='backlink' onClick={() => nav('/app/contas-a-pagar')}>
        <ChevronLeft /> {t('payables.back')}
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{p.description}</h1>
          <div className='detail-sub'>
            {t('payables.sub')} · {p.counterpartyName || '—'}
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
