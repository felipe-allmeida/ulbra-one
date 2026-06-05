import { useEffect, useState } from 'react';
import { Plus, Search, UserSquare, Power, Trash2, Pencil } from 'lucide-react';
import { api, PersonType, type Customer } from '../api';
import { useApp } from '../store';
import { Button, Drawer, ConfirmDialog, EmptyState, PageHeader, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { useAsync } from '../useAsync';
import { maskCpf, maskCnpj } from '../format';
import { brl } from '../lib';
import { EmpresaPicker } from './shared';
import { CustomerForm } from './registry-forms';

export function ClientesPage() {
  const { empresa, empresaId } = useApp();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [drawer, setDrawer] = useState<null | { edit?: Customer }>(null);
  const [confirm, setConfirm] = useState<null | { customer: Customer; action: 'toggle' | 'delete' }>(null);
  const [busy, setBusy] = useState(false);

  const list = useAsync<Customer[]>(() => api.listCustomers(empresaId, search));
  const reload = () => {
    if (empresaId) list.run();
  };
  useEffect(() => {
    if (!empresaId) return;
    const t = setTimeout(reload, 250);
    return () => clearTimeout(t);
  }, [empresaId, search]);

  const customers = list.data ?? [];

  const onConfirm = async () => {
    if (!confirm) return;
    setBusy(true);
    try {
      const { customer, action } = confirm;
      if (action === 'delete') {
        await api.removeCustomer(customer.id);
        toast.success('Cliente excluído.');
      } else if (customer.active) {
        await api.deactivateCustomer(customer.id);
        toast.success('Cliente desativado.');
      } else {
        await api.activateCustomer(customer.id);
        toast.success('Cliente ativado.');
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
      <PageHeader title='Clientes' sub='Pessoas físicas e jurídicas que compram produtos ou serviços do grupo.'>
        <EmpresaPicker />
        <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => setDrawer({})}>
          Novo cliente
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<UserSquare />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : (
        <Panel
          title={`${customers.length} cliente(s)`}
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
                <th>Limite de crédito</th>
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
              ) : customers.length === 0 ? (
                <tr>
                  <td colSpan={6} className='t-empty'>
                    Nenhum cliente encontrado.
                  </td>
                </tr>
              ) : (
                customers.map((c) => (
                  <tr key={c.id} className='row-click' onClick={() => setDrawer({ edit: c })}>
                    <td>
                      {c.name}
                      {c.tradeName && <span className='cc-code'> · {c.tradeName}</span>}
                    </td>
                    <td className='t-mono'>
                      {c.personType === PersonType.Company ? maskCnpj(c.document) : maskCpf(c.document)}
                    </td>
                    <td>{c.personType === PersonType.Company ? 'PJ' : 'PF'}</td>
                    <td className='t-mono'>{c.creditLimit != null ? brl(c.creditLimit) : '—'}</td>
                    <td>
                      <span className={`badge-status ${c.active ? 'badge-pago' : 'badge-rejeitado'}`}>
                        {c.active ? 'Ativo' : 'Inativo'}
                      </span>
                    </td>
                    <td className='td-action' onClick={(e) => e.stopPropagation()}>
                      <button className='icon-btn' onClick={() => setDrawer({ edit: c })} aria-label='Editar'>
                        <Pencil />
                      </button>
                      <button
                        className='icon-btn'
                        onClick={() => setConfirm({ customer: c, action: 'toggle' })}
                        aria-label={c.active ? 'Desativar' : 'Ativar'}
                      >
                        <Power />
                      </button>
                      <button
                        className='icon-btn'
                        onClick={() => setConfirm({ customer: c, action: 'delete' })}
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
        title={drawer?.edit ? 'Editar cliente' : 'Novo cliente'}
        sub={empresa?.legalName}
        onClose={() => setDrawer(null)}
      >
        {drawer && empresa && (
          <CustomerForm
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
              ? 'Excluir cliente'
              : confirm.customer.active
                ? 'Desativar cliente'
                : 'Ativar cliente'
          }
          message={
            confirm.action === 'delete' ? (
              <>
                Excluir <b>{confirm.customer.name}</b>? Esta ação remove o cadastro (soft-delete).
              </>
            ) : confirm.customer.active ? (
              <>
                Desativar <b>{confirm.customer.name}</b>? Ele deixa de aparecer como ativo.
              </>
            ) : (
              <>
                Reativar <b>{confirm.customer.name}</b>?
              </>
            )
          }
          danger={confirm.action === 'delete'}
          busy={busy}
          confirmLabel={confirm.action === 'delete' ? 'Excluir' : confirm.customer.active ? 'Desativar' : 'Ativar'}
          onConfirm={onConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}
