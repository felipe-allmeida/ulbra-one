import { useEffect, useState } from 'react';
import { Plus, Search, UserSquare, Power, Trash2, Pencil } from 'lucide-react';
import { PersonType, type Customer } from '../api';
import { getErrorMessage } from '../apiError';
import { useApp } from '../store';
import { useCustomerAction, useCustomers } from '../queries';
import { Button, Drawer, ConfirmDialog, EmptyState, PageHeader, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { maskCpf, maskCnpj } from '../format';
import { brl } from '../lib';
import { EmpresaPicker } from './shared';
import { CustomerForm } from './registry-forms';

export function ClientesPage() {
  const { empresa, empresaId } = useApp();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [drawer, setDrawer] = useState<null | { edit?: Customer }>(null);
  const [confirm, setConfirm] = useState<null | { customer: Customer; action: 'toggle' | 'delete' }>(null);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(t);
  }, [search]);

  const list = useCustomers(empresaId, debouncedSearch);
  const customers = list.data ?? [];

  const action = useCustomerAction({
    onSuccess: (_data, vars) => {
      toast.success(
        vars.action === 'delete'
          ? 'Cliente excluído.'
          : vars.action === 'deactivate'
            ? 'Cliente desativado.'
            : 'Cliente ativado.',
      );
      setConfirm(null);
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const onConfirm = () => {
    if (!confirm) return;
    const { customer, action: act } = confirm;
    const resolved = act === 'delete' ? 'delete' : customer.active ? 'deactivate' : 'activate';
    action.mutate({ id: customer.id, action: resolved });
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
            onSaved={() => setDrawer(null)}
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
          busy={action.isPending}
          confirmLabel={confirm.action === 'delete' ? 'Excluir' : confirm.customer.active ? 'Desativar' : 'Ativar'}
          onConfirm={onConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}
