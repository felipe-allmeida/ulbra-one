import { useEffect, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Plus, Search, Truck, Power, Trash2, Pencil } from 'lucide-react';
import { PersonType, type Vendor } from '../api';
import { getErrorMessage } from '../apiError';
import { useApp } from '../store';
import { useVendor, useVendorAction, useVendors } from '../queries';
import { Button, ConfirmDialog, EmptyState, PageHeader, Pager, Panel, LoadingState } from '../ui';
import { useToast } from '../toast';
import { P, usePermissions } from '../permissions';
import { maskCpf, maskCnpj } from '../format';
import { BackLink, useOriginNav, usePageParam, useResetPageOnChange } from '../nav';
import { VendorForm } from './registry-forms';
import { VendorHistory } from './vendor-history';
import { StageTabBar, type StageTab } from '../components/workflow';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';

const vendorDoc = (v: Vendor) => (v.personType === PersonType.Company ? maskCnpj(v.document) : maskCpf(v.document));

export function FornecedoresPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const openDetail = useOriginNav();
  const toast = useToast();
  const canWrite = usePermissions().can(P.registryWrite);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [page, setPage] = usePageParam();
  const [confirm, setConfirm] = useState<null | { vendor: Vendor; action: 'toggle' | 'delete' }>(null);

  // Debounce the search term that feeds the query key (preserves the 250ms UX).
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(t);
  }, [search]);

  // Any change to the company or active search resets paging to the first page (but not on mount, so
  // returning from a vendor detail keeps the restored ?page=).
  useResetPageOnChange(setPage, [empresaId, debouncedSearch]);

  const list = useVendors(debouncedSearch, page, !!empresaId);
  const vendors = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;
  const total = list.data?.total ?? 0;

  const action = useVendorAction({
    onSuccess: (_data, vars) => {
      toast.success(
        vars.action === 'delete'
          ? 'Fornecedor excluído.'
          : vars.action === 'deactivate'
            ? 'Fornecedor desativado.'
            : 'Fornecedor ativado.',
      );
      setConfirm(null);
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const onConfirm = () => {
    if (!confirm) return;
    const { vendor, action: act } = confirm;
    const resolved = act === 'delete' ? 'delete' : vendor.active ? 'deactivate' : 'activate';
    action.mutate({ id: vendor.id, action: resolved });
  };

  return (
    <>
      <PageHeader title='Fornecedores' sub='Pessoas físicas e jurídicas que fornecem produtos ou serviços ao grupo.'>
        {canWrite && (
          <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => nav('/app/vendors/new')}>
            Novo fornecedor
          </Button>
        )}
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Truck />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : (
        <Panel
          title={`${total} fornecedor(es)`}
          action={
            <div className='top-search' style={{ width: 260 }}>
              <Search />
              <input
                placeholder='Buscar por nome ou documento…'
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
          }
        >
          {list.isLoading ? (
            <LoadingState />
          ) : list.error ? (
            <EmptyState
              icon={<Truck />}
              title={t('common.loadError')}
              sub={getErrorMessage(list.error)}
              action={
                <button className='backlink' onClick={() => void list.refetch()}>
                  {t('common.retry')}
                </button>
              }
            />
          ) : vendors.length === 0 ? (
            <table>
              <tbody>
                <tr>
                  <td className='t-empty'>Nenhum fornecedor encontrado.</td>
                </tr>
              </tbody>
            </table>
          ) : (
            <VendorsTable
              vendors={vendors}
              canWrite={canWrite}
              openDetail={openDetail}
              onEdit={(v) => openDetail(`/app/vendors/${v.id}`)}
              onToggle={(v) => setConfirm({ vendor: v, action: 'toggle' })}
              onDelete={(v) => setConfirm({ vendor: v, action: 'delete' })}
            />
          )}
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </Panel>
      )}

      {confirm && (
        <ConfirmDialog
          title={
            confirm.action === 'delete'
              ? 'Excluir fornecedor'
              : confirm.vendor.active
                ? 'Desativar fornecedor'
                : 'Ativar fornecedor'
          }
          message={
            confirm.action === 'delete' ? (
              <>
                Excluir <b>{confirm.vendor.name}</b>? Esta ação remove o cadastro (soft-delete).
              </>
            ) : confirm.vendor.active ? (
              <>
                Desativar <b>{confirm.vendor.name}</b>? Ele deixa de aparecer como ativo.
              </>
            ) : (
              <>
                Reativar <b>{confirm.vendor.name}</b>?
              </>
            )
          }
          danger={confirm.action === 'delete'}
          busy={action.isPending}
          confirmLabel={confirm.action === 'delete' ? 'Excluir' : confirm.vendor.active ? 'Desativar' : 'Ativar'}
          onConfirm={onConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}

/**
 * Ordem = nome/documento/tipo/contato/status/ações, igual a hoje. `name` (+ tradeName) é o `title`;
 * `documento` e `contato` são os dois `meta` (identificam e permitem alcançar o fornecedor de
 * relance); `tipo` (PF/PJ) fica em `detail` (orçamento de meta já usado); `status` é badge; `ações`
 * (editar/ativar-desativar/excluir) ganha `align: 'right'` — o `<td>` original levava
 * `.td-action` (width:1%, sem quebra, alinhado à direita) e `header: ''` porque o `<th>` original era
 * vazio.
 */
function VendorsTable({
  vendors,
  canWrite,
  openDetail,
  onEdit,
  onToggle,
  onDelete,
}: {
  vendors: Vendor[];
  canWrite: boolean;
  openDetail: (path: string) => void;
  onEdit: (v: Vendor) => void;
  onToggle: (v: Vendor) => void;
  onDelete: (v: Vendor) => void;
}) {
  const columns: Column<Vendor>[] = [
    {
      key: 'name',
      header: 'Nome',
      role: 'title',
      cell: (v) => (
        <>
          {v.name}
          {v.tradeName && <span className='cc-code'> · {v.tradeName}</span>}
        </>
      ),
    },
    {
      key: 'document',
      header: 'Documento',
      role: 'meta',
      cell: (v) => <span className='t-mono'>{vendorDoc(v)}</span>,
    },
    {
      key: 'personType',
      header: 'Tipo',
      role: 'detail',
      cell: (v) => (v.personType === PersonType.Company ? 'PJ' : 'PF'),
    },
    {
      key: 'contact',
      header: 'Contato',
      role: 'meta',
      cell: (v) => v.email || v.phone || '—',
    },
    {
      key: 'status',
      header: 'Status',
      role: 'status',
      cell: (v) => (
        <span className={`badge-status ${v.active ? 'badge-pago' : 'badge-rejeitado'}`}>
          {v.active ? 'Ativo' : 'Inativo'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (v) => {
        if (!canWrite) return null;
        return (
          <>
            <button className='btn btn-icon' onClick={() => onEdit(v)} aria-label='Editar' title='Editar'>
              <Pencil />
            </button>
            <button
              className='btn btn-icon'
              onClick={() => onToggle(v)}
              aria-label={v.active ? 'Desativar' : 'Ativar'}
              title={v.active ? 'Desativar' : 'Ativar'}
            >
              <Power />
            </button>
            <button className='btn btn-icon' onClick={() => onDelete(v)} aria-label='Excluir' title='Excluir'>
              <Trash2 />
            </button>
          </>
        );
      },
    },
  ];

  return (
    <ResponsiveTable
      rows={vendors}
      columns={columns}
      rowKey={(v) => v.id}
      onRowClick={(v) => openDetail(`/app/vendors/${v.id}`)}
    />
  );
}

/* ===================== DETAIL / NEW ===================== */
export function VendorDetailPage({ mode }: { mode: 'new' | 'detail' }) {
  const { id } = useParams();
  const nav = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  // Origem: triagem de uma requisição → o form ganha "Voltar à Requisição" que retorna à requisição aberta.
  const fromRequisition = searchParams.get('fromRequisition');
  const { empresa } = useApp();

  const vendorQuery = useVendor(mode === 'detail' ? id : undefined);
  const vendor = mode === 'detail' ? (vendorQuery.data ?? null) : null;
  const loading = mode === 'detail' && vendorQuery.isLoading;

  /* ---- NEW ---- */
  if (mode === 'new') {
    return (
      <>
        <BackLink fallback='/app/vendors' />
        <div className='detail-head'>
          <div>
            <h1 className='detail-title'>Novo fornecedor</h1>
            <div className='detail-sub'>{empresa?.legalName ?? 'Selecione uma empresa'}</div>
          </div>
        </div>
        {empresa ? (
          <div className='card-form vendor-page-form'>
            <VendorForm
              companyId={empresa.id}
              onSaved={(v) => nav(`/app/vendors/${v.id}`)}
              onCancel={() => nav('/app/vendors')}
            />
          </div>
        ) : (
          <EmptyState icon={<Truck />} title='Nenhuma empresa' sub='Cadastre uma empresa primeiro.' />
        )}
      </>
    );
  }

  /* ---- DETAIL ---- */
  if (loading) return <LoadingState />;
  if (!vendor)
    return <EmptyState icon={<Truck />} title='Fornecedor não encontrado' sub='Ele pode ter sido removido.' />;

  const tab = searchParams.get('tab') === 'historico' ? 'historico' : 'dados';
  const setTab = (key: string) =>
    setSearchParams(
      (prev) => {
        prev.set('tab', key);
        return prev;
      },
      { replace: true },
    );
  const tabs: StageTab[] = [
    { key: 'dados', label: 'Dados' },
    { key: 'historico', label: 'Histórico' },
  ];

  return (
    <>
      <BackLink fallback='/app/vendors' />
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>
            <Truck size={20} /> {vendor.name}
          </h1>
          <div className='detail-sub'>{vendorDoc(vendor)}</div>
        </div>
      </div>

      <StageTabBar tabs={tabs} active={tab} onChange={setTab} />

      {tab === 'dados' ? (
        <div className='card-form vendor-page-form'>
          <VendorForm
            companyId={empresa?.id ?? ''}
            initial={vendor}
            onSaved={() => void vendorQuery.refetch()}
            onCancel={() => nav('/app/vendors')}
            backToRequisition={fromRequisition ? () => nav(`/app/requisitions/${fromRequisition}`) : undefined}
          />
        </div>
      ) : (
        <VendorHistory vendor={vendor} />
      )}
    </>
  );
}
