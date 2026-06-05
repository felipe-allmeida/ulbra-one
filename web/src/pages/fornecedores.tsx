import { useEffect, useState } from 'react';
import { Plus, Search, Truck, Power, Trash2, Pencil } from 'lucide-react';
import { api, PersonType, type Vendor } from '../api';
import { useApp } from '../store';
import { Button, Drawer, ConfirmDialog, EmptyState, PageHeader, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { useAsync } from '../useAsync';
import { maskCpf, maskCnpj } from '../format';
import { EmpresaPicker } from './shared';
import { VendorForm } from './registry-forms';

export function FornecedoresPage() {
  const { empresa, empresaId } = useApp();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [drawer, setDrawer] = useState<null | { edit?: Vendor }>(null);
  const [confirm, setConfirm] = useState<null | { vendor: Vendor; action: 'toggle' | 'delete' }>(null);
  const [busy, setBusy] = useState(false);

  const list = useAsync<Vendor[]>(() => api.listVendors(empresaId, search));
  const reload = () => {
    if (empresaId) list.run();
  };
  useEffect(() => {
    if (!empresaId) return;
    const t = setTimeout(reload, 250);
    return () => clearTimeout(t);
  }, [empresaId, search]);

  const vendors = list.data ?? [];

  const onConfirm = async () => {
    if (!confirm) return;
    setBusy(true);
    try {
      const { vendor, action } = confirm;
      if (action === 'delete') {
        await api.removeVendor(vendor.id);
        toast.success('Fornecedor excluído.');
      } else if (vendor.active) {
        await api.deactivateVendor(vendor.id);
        toast.success('Fornecedor desativado.');
      } else {
        await api.activateVendor(vendor.id);
        toast.success('Fornecedor ativado.');
      }
      setConfirm(null);
      reload();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Falha na operação.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <PageHeader title='Fornecedores' sub='Pessoas físicas e jurídicas que fornecem produtos ou serviços ao grupo.'>
        <EmpresaPicker />
        <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => setDrawer({})}>
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
          title={`${vendors.length} fornecedor(es)`}
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
              {list.loading ? (
                <SkeletonRows rows={4} cols={6} />
              ) : list.error ? (
                <tr>
                  <td colSpan={6} className='t-empty'>
                    {list.error}{' '}
                    <button className='backlink' onClick={reload}>
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
                  <tr key={v.id} className='row-click' onClick={() => setDrawer({ edit: v })}>
                    <td>
                      {v.name}
                      {v.tradeName && <span className='cc-code'> · {v.tradeName}</span>}
                    </td>
                    <td className='t-mono'>
                      {v.personType === PersonType.Company ? maskCnpj(v.document) : maskCpf(v.document)}
                    </td>
                    <td>{v.personType === PersonType.Company ? 'PJ' : 'PF'}</td>
                    <td>{v.email || v.phone || '—'}</td>
                    <td>
                      <span className={`badge-status ${v.active ? 'badge-pago' : 'badge-rejeitado'}`}>
                        {v.active ? 'Ativo' : 'Inativo'}
                      </span>
                    </td>
                    <td className='td-action' onClick={(e) => e.stopPropagation()}>
                      <button className='icon-btn' onClick={() => setDrawer({ edit: v })} aria-label='Editar'>
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
        </Panel>
      )}

      <Drawer
        open={!!drawer}
        title={drawer?.edit ? 'Editar fornecedor' : 'Novo fornecedor'}
        sub={empresa?.legalName}
        onClose={() => setDrawer(null)}
      >
        {drawer && empresa && (
          <VendorForm
            companyId={empresa.id}
            initial={drawer.edit}
            onSaved={() => {
              setDrawer(null);
              reload();
            }}
            onCancel={() => setDrawer(null)}
          />
        )}
      </Drawer>

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
          busy={busy}
          confirmLabel={confirm.action === 'delete' ? 'Excluir' : confirm.vendor.active ? 'Desativar' : 'Ativar'}
          onConfirm={onConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}
