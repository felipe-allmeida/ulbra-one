import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Plus, Check, X, Wallet } from 'lucide-react';
import { type TransactionType, type TransactionStatus } from '../api';
import { BackLink, useOriginNav, usePageParam, useResetPageOnChange } from '../nav';
import { resolver, fieldError, transactionSchema, type TransactionValues } from './schemas';
import { useApp } from '../store';
import {
  useApproveTransaction,
  useCostCenters,
  useCreateTransaction,
  useCustomers,
  usePayTransaction,
  useRejectTransaction,
  useTransaction,
  useTransactions,
  useUnits,
  useVendors,
} from '../queries';
import { brl, parseMoneyInput } from '../lib';
import { getErrorMessage } from '../apiError';
import {
  Button,
  CurrencyInput,
  Input,
  Select,
  Field,
  PageHeader,
  Pager,
  Panel,
  EmptyState,
  Combobox,
  Drawer,
  LoadingState,
  SkeletonRows,
} from '../ui';
import { CustomerForm, VendorForm } from './registry-forms';
import { fmtDate } from '../datetime';

const STATUS: Record<TransactionStatus, { cls: string; label: string }> = {
  Pending: { cls: 'badge-pendente', label: 'Pendente' },
  Approved: { cls: 'badge-aprovado', label: 'Aprovado' },
  Paid: { cls: 'badge-pago', label: 'Pago' },
  Rejected: { cls: 'badge-rejeitado', label: 'Rejeitado' },
};
export const StatusBadge = ({ s }: { s: TransactionStatus }) => (
  <span className={`badge-status ${STATUS[s].cls}`}>{STATUS[s].label}</span>
);

/** Maps id→name of the company's units/cost centers (to show names in the lists). */
function useNomes(empresaId: string) {
  const unidades = useUnits(empresaId).data ?? [];
  const centros = useCostCenters(empresaId).data ?? [];
  return {
    unidades,
    centros,
    unidadeNome: useMemo(() => Object.fromEntries(unidades.map((u) => [u.id, u.acronym])), [unidades]),
    centroNome: useMemo(() => Object.fromEntries(centros.map((c) => [c.id, c.name])), [centros]),
  };
}

/* ============ LIST (Payables / receivables) ============ */
export function LancamentosPage({ tipo }: { tipo: TransactionType }) {
  const { empresa, empresaId } = useApp();
  const openDetail = useOriginNav();
  const [page, setPage] = usePageParam();
  const { unidadeNome, centroNome } = useNomes(empresaId);
  const list = useTransactions({ companyId: empresaId, type: tipo, page }, { enabled: !!empresaId });
  const itens = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  // Reset to the first page whenever the company or the list type changes (but not on mount, so returning
  // from an entry detail keeps the restored ?page=).
  useResetPageOnChange(setPage, [empresaId, tipo]);

  const titulo = tipo === 'Payable' ? 'Contas a pagar' : 'Contas a receber';

  return (
    <>
      <PageHeader
        title={titulo}
        sub={
          tipo === 'Payable'
            ? 'Despesas classificadas por natureza orçamentária e unidade.'
            : 'Receitas previstas e recebidas.'
        }
      >
        <Button
          variant='brand'
          icon={<Plus />}
          disabled={!empresa}
          onClick={() => openDetail(`/app/entries/new?tipo=${tipo}`)}
        >
          Novo lançamento
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState icon={<Wallet />} title='Nenhuma empresa' sub='Cadastre uma empresa em Estrutura.' />
      ) : list.isLoading ? (
        <Panel title={titulo}>
          <table>
            <thead>
              <tr>
                <th>Vencimento</th>
                <th>Descrição</th>
                <th>Contraparte</th>
                <th>Natureza orçamentária</th>
                <th>Unidade</th>
                <th style={{ textAlign: 'right' }}>Valor</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              <SkeletonRows rows={5} cols={7} />
            </tbody>
          </table>
        </Panel>
      ) : itens.length === 0 ? (
        <EmptyState
          icon={<Wallet />}
          title={`Nenhuma conta a ${tipo === 'Payable' ? 'pagar' : 'receber'}`}
          sub='Crie o primeiro lançamento.'
        />
      ) : (
        <Panel title={`${list.data?.total ?? itens.length} lançamento(s)`}>
          <table>
            <thead>
              <tr>
                <th>Vencimento</th>
                <th>Descrição</th>
                <th>Contraparte</th>
                <th>Natureza orçamentária</th>
                <th>Unidade</th>
                <th style={{ textAlign: 'right' }}>Valor</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {itens.map((l) => (
                <tr key={l.id} className='row-click' onClick={() => openDetail(`/app/entries/${l.id}`)}>
                  <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                  <td>{l.description}</td>
                  <td>{l.counterpartyName || '—'}</td>
                  <td>{centroNome[l.costCenterId] ?? '—'}</td>
                  <td className='t-mono'>{unidadeNome[l.unitId] ?? '—'}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(l.amount)}
                  </td>
                  <td>
                    <StatusBadge s={l.status} />
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

/* ============ NEW ============ */

/**
 * Counterparty picker: a searchable Combobox bound to vendors (Payable) or
 * customers (Receivable), with an inline "+ Novo" action that opens a Drawer to
 * create one and auto-selects it on success.
 */
function CounterpartyPicker({
  tipo,
  companyId,
  value,
  onChange,
  invalid,
}: {
  tipo: TransactionType;
  companyId: string;
  value: string | null;
  onChange: (id: string | null) => void;
  invalid?: boolean;
}) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [drawerOpen, setDrawerOpen] = useState(false);
  const isPayable = tipo === 'Payable';

  // Debounce the search term feeding the query (same 250ms UX as registry pages).
  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(id);
  }, [search]);

  // Vendors are global — only the payable/customer mode gates the fetch, not the selected company.
  const vendors = useVendors(debouncedSearch, undefined, isPayable);
  const customers = useCustomers(isPayable ? '' : companyId, debouncedSearch);
  const query = isPayable ? vendors : customers;
  const items = (query.data?.items ?? []).map((p) => ({
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
        loading={query.isLoading}
        placeholder={
          isPayable ? t('transactions.counterparty.selectVendor') : t('transactions.counterparty.selectCustomer')
        }
        searchPlaceholder={
          isPayable ? t('transactions.counterparty.searchVendor') : t('transactions.counterparty.searchCustomer')
        }
        emptyText={
          isPayable ? t('transactions.counterparty.emptyVendor') : t('transactions.counterparty.emptyCustomer')
        }
        onSearch={setSearch}
        footer={
          <Button type='button' variant='ghost' icon={<Plus />} onClick={() => setDrawerOpen(true)}>
            {isPayable ? t('transactions.counterparty.newVendor') : t('transactions.counterparty.newCustomer')}
          </Button>
        }
      />
      <Drawer
        open={drawerOpen}
        title={
          isPayable ? t('transactions.counterparty.newVendorTitle') : t('transactions.counterparty.newCustomerTitle')
        }
        onClose={() => setDrawerOpen(false)}
      >
        {drawerOpen &&
          (isPayable ? (
            <VendorForm
              companyId={companyId}
              onSaved={(v) => {
                onChange(v.id);
                setDrawerOpen(false);
              }}
              onCancel={() => setDrawerOpen(false)}
            />
          ) : (
            <CustomerForm
              companyId={companyId}
              onSaved={(c) => {
                onChange(c.id);
                setDrawerOpen(false);
              }}
              onCancel={() => setDrawerOpen(false)}
            />
          ))}
      </Drawer>
    </>
  );
}

export function NovoLancamentoPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { t } = useTranslation();
  const [sp] = useSearchParams();
  const { unidades, centros } = useNomes(empresaId);

  const { control, handleSubmit, watch, setValue, formState } = useForm<TransactionValues>({
    resolver: resolver(transactionSchema),
    mode: 'onChange',
    defaultValues: {
      type: (sp.get('tipo') as TransactionType) || 'Payable',
      unitId: '',
      costCenterId: '',
      description: '',
      vendorId: null,
      customerId: null,
      amount: '',
      dueDate: '',
    },
  });
  const tipo = watch('type');

  const create = useCreateTransaction({ onSuccess: (l) => nav(`/app/entries/${l.id}`) });
  const salvando = create.isPending;
  const erro = create.error ? getErrorMessage(create.error) : null;
  const onValid = (v: TransactionValues) =>
    create.mutate({
      companyId: empresaId,
      unitId: v.unitId,
      costCenterId: v.costCenterId,
      type: v.type,
      description: v.description.trim(),
      vendorId: v.type === 'Payable' ? v.vendorId : undefined,
      customerId: v.type === 'Receivable' ? v.customerId : undefined,
      amount: parseMoneyInput(v.amount) ?? 0,
      dueDate: v.dueDate,
    });

  return (
    <>
      <BackLink fallback={tipo === 'Payable' ? '/app/accounts-payable' : '/app/accounts-receivable'} />
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>Novo lançamento</h1>
          <div className='detail-sub'>{empresa?.legalName ?? 'Selecione uma empresa'}</div>
        </div>
      </div>

      {!empresa ? (
        <EmptyState icon={<Wallet />} title='Nenhuma empresa' sub='Cadastre uma empresa primeiro.' />
      ) : (
        <form className='card-form' onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
          <div className='edit-grid'>
            <Field label='Tipo'>
              <Controller
                control={control}
                name='type'
                render={({ field }) => (
                  <Select
                    {...field}
                    onChange={(e) => {
                      field.onChange(e);
                      // Switching type invalidates the previous counterparty selection.
                      setValue('vendorId', null, { shouldValidate: true });
                      setValue('customerId', null, { shouldValidate: true });
                    }}
                  >
                    <option value='Payable'>Conta a pagar</option>
                    <option value='Receivable'>Conta a receber</option>
                  </Select>
                )}
              />
            </Field>
            <Field label='Vencimento'>
              <Controller control={control} name='dueDate' render={({ field }) => <Input type='date' {...field} />} />
              {fieldError(formState.errors.dueDate?.message) && (
                <div className='field-err'>{fieldError(formState.errors.dueDate?.message)}</div>
              )}
            </Field>
            <Field label='Unidade'>
              <Controller
                control={control}
                name='unitId'
                render={({ field }) => (
                  <Select {...field}>
                    <option value=''>— selecione —</option>
                    {unidades.map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.name} ({u.acronym})
                      </option>
                    ))}
                  </Select>
                )}
              />
              {fieldError(formState.errors.unitId?.message) && (
                <div className='field-err'>{fieldError(formState.errors.unitId?.message)}</div>
              )}
            </Field>
            <Field label='Natureza orçamentária'>
              <Controller
                control={control}
                name='costCenterId'
                render={({ field }) => (
                  <Select {...field}>
                    <option value=''>— selecione —</option>
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
            <Field label='Valor (R$)'>
              <Controller
                control={control}
                name='amount'
                render={({ field }) => (
                  <CurrencyInput
                    value={field.value}
                    onValueChange={field.onChange}
                    onBlur={field.onBlur}
                    name={field.name}
                    placeholder='ex.: 3.000,00'
                  />
                )}
              />
              {fieldError(formState.errors.amount?.message) && (
                <div className='field-err'>{fieldError(formState.errors.amount?.message)}</div>
              )}
            </Field>
            <Field
              label={
                tipo === 'Payable'
                  ? t('transactions.counterparty.vendorLabel')
                  : t('transactions.counterparty.customerLabel')
              }
            >
              <Controller
                control={control}
                name={tipo === 'Payable' ? 'vendorId' : 'customerId'}
                render={({ field }) => (
                  <CounterpartyPicker
                    tipo={tipo}
                    companyId={empresaId}
                    value={field.value ?? null}
                    onChange={(id) => field.onChange(id)}
                    invalid={tipo === 'Payable' ? !!formState.errors.vendorId : !!formState.errors.customerId}
                  />
                )}
              />
              {fieldError((tipo === 'Payable' ? formState.errors.vendorId : formState.errors.customerId)?.message) && (
                <div className='field-err'>
                  {fieldError((tipo === 'Payable' ? formState.errors.vendorId : formState.errors.customerId)?.message)}
                </div>
              )}
            </Field>
          </div>
          <div style={{ marginTop: 14 }}>
            <Field label='Descrição'>
              <Controller
                control={control}
                name='description'
                render={({ field }) => <Input {...field} placeholder='ex.: Campanha de captação' />}
              />
              {fieldError(formState.errors.description?.message) && (
                <div className='field-err'>{fieldError(formState.errors.description?.message)}</div>
              )}
            </Field>
          </div>
          {erro && (
            <div className='create-err' style={{ marginTop: 10 }}>
              {erro}
            </div>
          )}
          <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
            <Button type='submit' variant='brand' disabled={!formState.isValid || salvando}>
              {salvando ? 'Salvando…' : 'Lançar'}
            </Button>
            <Button type='button' onClick={() => nav(-1)}>
              Cancelar
            </Button>
          </div>
        </form>
      )}
    </>
  );
}

/* ============ DETAIL ============ */
export function LancamentoDetailPage() {
  const { id } = useParams();
  const query = useTransaction(id);
  const l = query.data ?? null;
  const carregando = query.isLoading;

  const units = useUnits(l?.companyId ?? '').data ?? [];
  const centros = useCostCenters(l?.companyId ?? '').data ?? [];
  const nomes = {
    centro: centros.find((c) => c.id === l?.costCenterId)?.name ?? '—',
    unidade: units.find((u) => u.id === l?.unitId)?.name ?? '—',
  };

  const approve = useApproveTransaction();
  const reject = useRejectTransaction();
  const pay = usePayTransaction();

  if (carregando) return <LoadingState />;
  if (!l) return <EmptyState icon={<Wallet />} title='Lançamento não encontrado' sub='Ele pode ter sido removido.' />;

  return (
    <>
      <BackLink fallback={l.type === 'Payable' ? '/app/accounts-payable' : '/app/accounts-receivable'} />
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{l.description}</h1>
          <div className='detail-sub'>
            {l.type === 'Payable' ? 'Conta a pagar' : 'Conta a receber'} · {l.counterpartyName || '—'}
          </div>
        </div>
        <div className='detail-actions'>
          {l.status === 'Pending' && (
            <Button className='btn-approve' icon={<Check />} onClick={() => approve.mutate({ id: l.id })}>
              Aprovar
            </Button>
          )}
          {l.status === 'Pending' && (
            <Button icon={<X />} onClick={() => reject.mutate({ id: l.id, reason: 'Rejeitado' })}>
              Rejeitar
            </Button>
          )}
          {l.status === 'Approved' && (
            <Button variant='brand' onClick={() => pay.mutate({ id: l.id })}>
              Marcar como pago
            </Button>
          )}
        </div>
      </div>

      <div className='info-grid'>
        <div className='info-card'>
          <div className='info-label'>Valor</div>
          <div className='info-value'>{brl(l.amount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>Vencimento</div>
          <div className='info-value'>{fmtDate(l.dueDate)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>Status</div>
          <div style={{ marginTop: 4 }}>
            <StatusBadge s={l.status} />
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>Natureza orçamentária</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {nomes.centro}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>Unidade</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {nomes.unidade}
          </div>
        </div>
      </div>

      {l.rejectionReason && <p className='create-err'>Motivo da rejeição: {l.rejectionReason}</p>}
    </>
  );
}
