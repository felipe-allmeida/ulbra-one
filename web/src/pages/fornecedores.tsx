import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Plus, Search, Truck, Power, Trash2, Pencil, ChevronLeft } from 'lucide-react';
import { PersonType, type Vendor } from '../api';
import { getErrorMessage } from '../apiError';
import { useApp } from '../store';
import { useVendor, useVendorAction, useVendors } from '../queries';
import { Button, ConfirmDialog, EmptyState, PageHeader, Pager, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { maskCpf, maskCnpj } from '../format';
import { EmpresaPicker } from './shared';
import { VendorForm } from './registry-forms';

const vendorDoc = (v: Vendor) => (v.personType === PersonType.Company ? maskCnpj(v.document) : maskCpf(v.document));

export function FornecedoresPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [page, setPage] = useState(1);
  const [confirm, setConfirm] = useState<null | { vendor: Vendor; action: 'toggle' | 'delete' }>(null);

  // Debounce the search term that feeds the query key (preserves the 250ms UX).
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(t);
  }, [search]);

  // Any change to the company or active search resets paging to the first page.
  useEffect(() => setPage(1), [empresaId, debouncedSearch]);

  const list = useVendors(empresaId, debouncedSearch, page);
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
        <EmpresaPicker />
        <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => nav('/app/vendors/new')}>
          Novo fornecedor
        </Button>
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
          <table>
            <thead>
              <tr>
                <th>Nome</th>
                <th>Documento</th>
                <th>Tipo</th>
                <th>Contato</th>
                <th>Status</th>
                <th className='td-action'></th>
              </tr>
            </thead>
            <tbody>
              {list.isLoading ? (
                <SkeletonRows rows={4} cols={6} />
              ) : list.error ? (
                <tr>
                  <td colSpan={6} className='t-empty'>
                    {getErrorMessage(list.error)}{' '}
                    <button className='backlink' onClick={() => void list.refetch()}>
                      Tentar de novo
                    </button>
                  </td>
                </tr>
              ) : vendors.length === 0 ? (
                <tr>
                  <td colSpan={6} className='t-empty'>
                    Nenhum fornecedor encontrado.
                  </td>
                </tr>
              ) : (
                vendors.map((v) => (
                  <tr key={v.id} className='row-click' onClick={() => nav(`/app/vendors/${v.id}`)}>
                    <td>
                      {v.name}
                      {v.tradeName && <span className='cc-code'> · {v.tradeName}</span>}
                    </td>
                    <td className='t-mono'>{vendorDoc(v)}</td>
                    <td>{v.personType === PersonType.Company ? 'PJ' : 'PF'}</td>
                    <td>{v.email || v.phone || '—'}</td>
                    <td>
                      <span className={`badge-status ${v.active ? 'badge-pago' : 'badge-rejeitado'}`}>
                        {v.active ? 'Ativo' : 'Inativo'}
                      </span>
                    </td>
                    <td className='td-action' onClick={(e) => e.stopPropagation()}>
                      <button className='icon-btn' onClick={() => nav(`/app/vendors/${v.id}`)} aria-label='Editar'>
                        <Pencil />
                      </button>
                      <button
                        className='icon-btn'
                        onClick={() => setConfirm({ vendor: v, action: 'toggle' })}
                        aria-label={v.active ? 'Desativar' : 'Ativar'}
                      >
                        <Power />
                      </button>
                      <button
                        className='icon-btn'
                        onClick={() => setConfirm({ vendor: v, action: 'delete' })}
                        aria-label='Excluir'
                      >
                        <Trash2 />
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
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

/* ===================== DETAIL / NEW ===================== */
export function VendorDetailPage({ mode }: { mode: 'new' | 'detail' }) {
  const { id } = useParams();
  const nav = useNavigate();
  const { empresa, empresas } = useApp();

  const vendorQuery = useVendor(mode === 'detail' ? id : undefined);
  const vendor = mode === 'detail' ? (vendorQuery.data ?? null) : null;
  const loading = mode === 'detail' && vendorQuery.isLoading;

  // The vendor's company (in detail) or the selected company (in new).
  const vendorCompany = vendor ? (empresas.find((e) => e.id === vendor.companyId) ?? null) : empresa;

  /* ---- NEW ---- */
  if (mode === 'new') {
    return (
      <>
        <button className='backlink' onClick={() => nav('/app/vendors')}>
          <ChevronLeft /> Fornecedores
        </button>
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
  if (loading) return <div className='empty-state'>Carregando…</div>;
  if (!vendor || !vendorCompany)
    return <EmptyState icon={<Truck />} title='Fornecedor não encontrado' sub='Ele pode ter sido removido.' />;

  return (
    <>
      <button className='backlink' onClick={() => nav('/app/vendors')}>
        <ChevronLeft /> Fornecedores
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>
            <Truck size={20} /> {vendor.name}
          </h1>
          <div className='detail-sub'>
            {vendorDoc(vendor)} · {vendorCompany.legalName}
          </div>
        </div>
      </div>

      <div className='card-form vendor-page-form'>
        <VendorForm
          companyId={vendor.companyId}
          initial={vendor}
          onSaved={() => void vendorQuery.refetch()}
          onCancel={() => nav('/app/vendors')}
        />
      </div>
    </>
  );
}
