import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Plus, ChevronLeft, HandCoins, Pencil, X, Download, Receipt as ReceiptIcon, Undo2 } from 'lucide-react';
import {
  api,
  auth,
  type Receivable,
  type ReceivableInput,
  type ReceivableStatus,
  type ReceivableInstallment,
  type ReceivableInstallmentStatus,
  type Receipt,
  type PaymentMethod,
  type RegisterReceiptInput,
} from '../api';
import {
  resolver,
  fieldError,
  receivableSchema,
  receiptSchema,
  previewInstallments,
  type ReceivableValues,
  type ReceiptValues,
} from './schemas';
import { useApp } from '../store';
import {
  type ReceivablesParams,
  useBankAccounts,
  useCancelReceivable,
  useCostCenters,
  useCreateReceivable,
  useCustomers,
  useReceivable,
  useReceivables,
  useRegisterReceipt,
  useReverseReceipt,
  useUpdateReceivable,
  useUploadReceiptProof,
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
import { CustomerForm } from './registry-forms';

const fmtData = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');

const toDateOnly = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

type TabKey = 'all' | 'overdue' | 'thisMonth' | 'future' | 'received';

/** Maps a tab to ListReceivables filters. Computed at call time so "today" stays correct past midnight. */
function tabToParams(tab: TabKey): Pick<ReceivablesParams, 'received' | 'dueFrom' | 'dueTo'> {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  switch (tab) {
    case 'overdue': // não recebidas, vencimento já passou (até ontem)
      return {
        received: false,
        dueTo: toDateOnly(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1)),
      };
    case 'thisMonth': // não recebidas, a vencer ainda neste mês (hoje → último dia do mês)
      return {
        received: false,
        dueFrom: toDateOnly(today),
        dueTo: toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 0)),
      };
    case 'future': // não recebidas, vencem em meses futuros (a partir do 1º dia do próximo mês)
      return { received: false, dueFrom: toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 1)) };
    case 'received': // parcelas recebidas (qualquer data)
      return { received: true };
    case 'all':
    default:
      return {};
  }
}

const TABS: TabKey[] = ['all', 'overdue', 'thisMonth', 'future', 'received'];

function ReceivableStatusBadge({ s }: { s: ReceivableStatus }) {
  const { t } = useTranslation();
  const cls: Record<ReceivableStatus, string> = {
    AwaitingReceipt: 'badge-pendente',
    Received: 'badge-pago',
    Cancelled: 'badge-rejeitado',
    WrittenOff: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`receivables.status.${s}`)}</span>;
}

function ReceivableInstallmentStatusBadge({ s }: { s: ReceivableInstallmentStatus }) {
  const { t } = useTranslation();
  const cls: Record<ReceivableInstallmentStatus, string> = {
    Open: 'badge-pendente',
    PartiallyReceived: 'badge-aprovado',
    Received: 'badge-pago',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`receivableInstallments.status.${s}`)}</span>;
}

function OverdueBadge() {
  const { t } = useTranslation();
  return <span className='badge-status badge-rejeitado'>{t('receivableInstallments.overdue')}</span>;
}

/** Maps id→name of the company's cost centers (to show names in lists). */
function useNames(companyId: string) {
  const centros = useCostCenters(companyId).data ?? [];
  return {
    centros,
    centroNome: useMemo(() => Object.fromEntries(centros.map((c) => [c.id, c.name])), [centros]),
  };
}

/* ============ LIST ============ */
export function ContasReceberPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [tab, setTab] = useState<TabKey>('all');
  const { centroNome } = useNames(empresaId);
  const filters = useMemo(() => tabToParams(tab), [tab]);
  const list = useReceivables({ companyId: empresaId, page, ...filters }, { enabled: !!empresaId });
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
      <PageHeader title={t('receivables.title')} sub={t('receivables.subtitle')}>
        <EmpresaPicker />
        <Button
          variant='brand'
          icon={<Plus />}
          disabled={!empresa}
          style={{ alignSelf: 'flex-end' }}
          onClick={() => nav('/app/contas-a-receber/novo')}
        >
          {t('receivables.new')}
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<HandCoins />}
          title={t('receivables.empty.noCompanyTitle')}
          sub={t('receivables.empty.noCompanySub')}
        />
      ) : (
        <>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
            {TABS.map((k) => (
              <Button key={k} variant={tab === k ? 'brand' : 'default'} onClick={() => changeTab(k)}>
                {t(`receivables.tabs.${k}`)}
              </Button>
            ))}
          </div>
          {itens.length === 0 ? (
            <EmptyState
              icon={<HandCoins />}
              title={t('receivables.empty.noItemsTitle')}
              sub={t('receivables.empty.noItemsSub')}
            />
          ) : (
            <Panel title={t('receivables.count', { count: list.data?.total ?? itens.length })}>
              <table>
                <thead>
                  <tr>
                    <th>{t('receivables.columns.dueDate')}</th>
                    <th>{t('receivables.columns.description')}</th>
                    <th>{t('receivables.columns.payer')}</th>
                    <th>{t('receivables.columns.installment')}</th>
                    <th>{t('receivables.columns.costCenter')}</th>
                    <th style={{ textAlign: 'right' }}>{t('receivables.columns.amount')}</th>
                    <th>{t('receivables.columns.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {itens.map((l) => (
                    <tr
                      key={l.installmentId}
                      className='row-click'
                      onClick={() => nav(`/app/contas-a-receber/${l.receivableId}`)}
                    >
                      <td className='t-mono' style={l.isOverdue ? { color: 'var(--danger, #dc2626)' } : undefined}>
                        {fmtData(l.dueDate)}
                      </td>
                      <td>{l.description}</td>
                      <td>{l.payerName || '—'}</td>
                      <td className='t-mono'>
                        {l.sequence}/{l.installmentCount}
                      </td>
                      <td>{centroNome[l.costCenterId] ?? '—'}</td>
                      <td className='amount-cell' style={{ textAlign: 'right' }}>
                        {brl(l.amount)}
                      </td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
                          <ReceivableStatusBadge s={l.receivableStatus} />
                          {l.receivableStatus === 'AwaitingReceipt' && (
                            <ReceivableInstallmentStatusBadge s={l.installmentStatus} />
                          )}
                          {l.isOverdue && <OverdueBadge />}
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

/* ============ Customer (payer) picker (Combobox + inline create) ============ */
function CustomerPicker({
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

  const customers = useCustomers(companyId, debouncedSearch);
  const items = (customers.data?.items ?? []).map((p) => ({
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
        loading={customers.isLoading}
        placeholder={t('transactions.counterparty.selectCustomer')}
        searchPlaceholder={t('transactions.counterparty.searchCustomer')}
        emptyText={t('transactions.counterparty.emptyCustomer')}
        onSearch={setSearch}
        footer={
          <Button type='button' variant='ghost' icon={<Plus />} onClick={() => setDrawerOpen(true)}>
            {t('transactions.counterparty.newCustomer')}
          </Button>
        }
      />
      <Drawer
        open={drawerOpen}
        title={t('transactions.counterparty.newCustomerTitle')}
        onClose={() => setDrawerOpen(false)}
      >
        {drawerOpen && (
          <CustomerForm
            companyId={companyId}
            onSaved={(c) => {
              onChange(c.id);
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
      update.mutate({ id: initial.id, body }, { onSuccess: (r) => nav(`/app/contas-a-receber/${r.id}`) });
    } else {
      create.mutate(body, { onSuccess: (r) => nav(`/app/contas-a-receber/${r.id}`) });
    }
  };

  const backTo = editing && initial ? `/app/contas-a-receber/${initial.id}` : '/app/contas-a-receber';

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

export function NovaContaReceberPage() {
  return <ReceivableForm />;
}

export function EditarContaReceberPage() {
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

/* ============ Receipt proof download (authed fetch → blob) ============ */
async function downloadReceiptProof(receivableId: string, receipt: Receipt): Promise<void> {
  const url = api.downloadReceiptProofUrl(receivableId, receipt.id);
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = `comprovante-${receipt.id}`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(objectUrl);
}

const RECEIPT_METHODS: PaymentMethod[] = ['Pix', 'Ted', 'Boleto', 'Cash', 'DirectDebit', 'Other'];

/* ============ Recebimento (register receipt) form ============ */
function ReceiptForm({
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

/* ============ Per-installment receipt history + actions ============ */
function ReceiptHistory({ receivable, installment }: { receivable: Receivable; installment: ReceivableInstallment }) {
  const { t } = useTranslation();
  const reverse = useReverseReceipt();
  const [toReverse, setToReverse] = useState<Receipt | null>(null);
  const [reverseReason, setReverseReason] = useState('');
  const [downloadErr, setDownloadErr] = useState<string | null>(null);

  const receipts = [...installment.receipts].sort((a, b) => a.receiptDate.localeCompare(b.receiptDate));
  if (receipts.length === 0) return null;

  return (
    <>
      <table>
        <thead>
          <tr>
            <th>{t('receipts.columns.date')}</th>
            <th style={{ textAlign: 'right' }}>{t('receipts.columns.amount')}</th>
            <th>{t('receipts.columns.method')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {receipts.map((rcpt) => (
            <tr key={rcpt.id}>
              <td className='t-mono'>{fmtData(rcpt.receiptDate)}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(rcpt.amount)}
              </td>
              <td>{t(`receipts.methods.${rcpt.method}`)}</td>
              <td className='td-action'>
                <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center' }}>
                  {rcpt.isReversed ? (
                    <span className='badge-status badge-rejeitado'>{t('receipts.reversed')}</span>
                  ) : (
                    <>
                      {rcpt.hasProof && (
                        <Button
                          icon={<Download />}
                          onClick={() => {
                            setDownloadErr(null);
                            downloadReceiptProof(receivable.id, rcpt).catch(() =>
                              setDownloadErr(t('receipts.downloadError')),
                            );
                          }}
                        >
                          {t('receipts.downloadProof')}
                        </Button>
                      )}
                      <Button
                        icon={<Undo2 />}
                        onClick={() => {
                          setReverseReason('');
                          setToReverse(rcpt);
                        }}
                      >
                        {t('receipts.reverse')}
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
          title={t('receipts.reverseDialog.title')}
          danger
          busy={reverse.isPending}
          confirmLabel={t('receipts.reverseDialog.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('receipts.reverseDialog.message')}</div>
              <Input
                autoFocus
                value={reverseReason}
                placeholder={t('receipts.reverseDialog.placeholder')}
                onChange={(e) => setReverseReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            reverse.mutate(
              { receivableId: receivable.id, receiptId: toReverse.id, reason: reverseReason.trim() },
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
export function ContaReceberDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const { t } = useTranslation();
  const query = useReceivable(id);
  const r = query.data ?? null;

  const centros = useCostCenters(r?.companyId ?? '').data ?? [];
  const cancel = useCancelReceivable();

  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [baixaInstallment, setBaixaInstallment] = useState<ReceivableInstallment | null>(null);

  if (query.isLoading) return <div className='empty-state'>{t('receivables.loading')}</div>;
  if (!r)
    return (
      <EmptyState
        icon={<HandCoins />}
        title={t('receivables.empty.notFoundTitle')}
        sub={t('receivables.empty.notFoundSub')}
      />
    );

  const centroNome = centros.find((c) => c.id === r.costCenterId)?.name ?? '—';
  const hasReceipts = r.installments.some((i) => i.receipts.some((rc) => !rc.isReversed));
  const editable = r.status === 'AwaitingReceipt' && !hasReceipts;

  return (
    <>
      <button className='backlink' onClick={() => nav('/app/contas-a-receber')}>
        <ChevronLeft /> {t('receivables.back')}
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{r.description}</h1>
          <div className='detail-sub'>
            {t('receivables.sub')} · {r.payerName || '—'}
          </div>
        </div>
        <div className='detail-actions'>
          {editable && (
            <Button icon={<Pencil />} onClick={() => nav(`/app/contas-a-receber/${r.id}/editar`)}>
              {t('receivables.actions.edit')}
            </Button>
          )}
          {editable && (
            <Button icon={<X />} onClick={() => setCancelOpen(true)}>
              {t('receivables.actions.cancel')}
            </Button>
          )}
        </div>
      </div>

      <div className='info-grid'>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.totalAmount')}</div>
          <div className='info-value'>{brl(r.totalAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.status')}</div>
          <div style={{ marginTop: 4 }}>
            <ReceivableStatusBadge s={r.status} />
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.costCenter')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {centroNome}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.payer')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {r.payerName || '—'}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.documentNumber')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {r.documentNumber || '—'}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.issueDate')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {r.issueDate ? fmtData(r.issueDate) : '—'}
          </div>
        </div>
      </div>

      {r.cancelReason && <p className='create-err'>{t('receivables.cancelReason', { reason: r.cancelReason })}</p>}

      <Panel title={t('receivables.installmentsTitle')}>
        <table>
          <thead>
            <tr>
              <th>{t('receivableInstallments.columns.sequence')}</th>
              <th>{t('receivableInstallments.columns.dueDate')}</th>
              <th style={{ textAlign: 'right' }}>{t('receivableInstallments.columns.amount')}</th>
              <th>{t('receivableInstallments.columns.status')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {[...r.installments]
              .sort((a, b) => a.sequence - b.sequence)
              .map((inst) => (
                <tr key={inst.id}>
                  <td className='t-mono'>
                    {inst.sequence}/{r.installments.length}
                  </td>
                  <td className='t-mono' style={inst.isOverdue ? { color: 'var(--danger, #dc2626)' } : undefined}>
                    {fmtData(inst.dueDate)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(inst.amount)}
                  </td>
                  <td>
                    <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
                      <ReceivableInstallmentStatusBadge s={inst.status} />
                      {inst.isOverdue && <OverdueBadge />}
                    </div>
                  </td>
                  <td className='td-action'>
                    {r.status === 'AwaitingReceipt' && inst.status !== 'Received' && (
                      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                        <Button icon={<ReceiptIcon />} onClick={() => setBaixaInstallment(inst)}>
                          {t('receipts.register')}
                        </Button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
          </tbody>
        </table>
      </Panel>

      {[...r.installments]
        .sort((a, b) => a.sequence - b.sequence)
        .filter((inst) => inst.receipts.length > 0)
        .map((inst) => (
          <Panel
            key={inst.id}
            title={`${t('receipts.title')} — ${t('receivableInstallments.label')} ${inst.sequence}/${r.installments.length}`}
          >
            <ReceiptHistory receivable={r} installment={inst} />
          </Panel>
        ))}

      {cancelOpen && (
        <ConfirmDialog
          title={t('receivables.cancel.title')}
          danger
          busy={cancel.isPending}
          confirmLabel={t('receivables.cancel.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('receivables.cancel.message')}</div>
              <Input
                autoFocus
                value={cancelReason}
                placeholder={t('receivables.cancel.placeholder')}
                onChange={(e) => setCancelReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            cancel.mutate({ id: r.id, reason: cancelReason.trim() }, { onSuccess: () => setCancelOpen(false) })
          }
          onCancel={() => setCancelOpen(false)}
        />
      )}

      <Drawer
        open={!!baixaInstallment}
        title={t('receipts.registerTitle')}
        sub={
          baixaInstallment
            ? `${t('receivableInstallments.label')} ${baixaInstallment.sequence}/${r.installments.length}`
            : undefined
        }
        onClose={() => setBaixaInstallment(null)}
      >
        {baixaInstallment && (
          <ReceiptForm
            receivable={r}
            installment={baixaInstallment}
            onDone={() => setBaixaInstallment(null)}
            onCancel={() => setBaixaInstallment(null)}
          />
        )}
      </Drawer>
    </>
  );
}
