import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Search, UserSquare, Power, Trash2, Pencil } from 'lucide-react';
import { PersonType, type Customer } from '../api';
import { getErrorMessage } from '../apiError';
import { useApp } from '../store';
import { useCustomerAction, useCustomers } from '../queries';
import { Button, Drawer, ConfirmDialog, EmptyState, PageHeader, Pager, Panel, LoadingState } from '../ui';
import { useToast } from '../toast';
import { P, usePermissions } from '../permissions';
import { maskCpf, maskCnpj } from '../format';
import { brl } from '../lib';
import { CustomerForm } from './registry-forms';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';

export function ClientesPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const toast = useToast();
  const canWrite = usePermissions().can(P.registryWrite);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [page, setPage] = useState(1);
  const [drawer, setDrawer] = useState<null | { edit?: Customer }>(null);
  const [confirm, setConfirm] = useState<null | { customer: Customer; action: 'toggle' | 'delete' }>(null);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(t);
  }, [search]);

  // Any change to the company or active search resets paging to the first page.
  useEffect(() => setPage(1), [empresaId, debouncedSearch]);

  const list = useCustomers(empresaId, debouncedSearch, page);
  const customers = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;
  const total = list.data?.total ?? 0;

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
        {canWrite && (
          <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => setDrawer({})}>
            Novo cliente
          </Button>
        )}
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<UserSquare />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : (
        <Panel
          title={`${total} cliente(s)`}
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
              icon={<UserSquare />}
              title={t('common.loadError')}
              sub={getErrorMessage(list.error)}
              action={
                <button className='backlink' onClick={() => void list.refetch()}>
                  {t('common.retry')}
                </button>
              }
            />
          ) : customers.length === 0 ? (
            <table>
              <tbody>
                <tr>
                  <td className='t-empty'>Nenhum cliente encontrado.</td>
                </tr>
              </tbody>
            </table>
          ) : (
            <CustomersTable
              customers={customers}
              canWrite={canWrite}
              onEdit={(c) => setDrawer({ edit: c })}
              onToggle={(c) => setConfirm({ customer: c, action: 'toggle' })}
              onDelete={(c) => setConfirm({ customer: c, action: 'delete' })}
            />
          )}
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
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

/**
 * Ordem = nome/documento/tipo/limite/status/ações, igual a hoje. `name` (+ tradeName) é o `title`;
 * `documento` e `tipo` são os dois `meta` (identificam de relance); `limite de crédito` é o único
 * valor monetário da linha e vira `amount`; `status` é badge; `ações` (editar/ativar-desativar/
 * excluir) ganha `align: 'right'` — o `<td>` original levava `.td-action` (width:1%,
 * sem quebra, alinhado à direita) e `header: ''` porque o `<th>` original era vazio.
 */
function CustomersTable({
  customers,
  canWrite,
  onEdit,
  onToggle,
  onDelete,
}: {
  customers: Customer[];
  canWrite: boolean;
  onEdit: (c: Customer) => void;
  onToggle: (c: Customer) => void;
  onDelete: (c: Customer) => void;
}) {
  const columns: Column<Customer>[] = [
    {
      key: 'name',
      header: 'Nome',
      role: 'title',
      cell: (c) => (
        <>
          {c.name}
          {c.tradeName && <span className='cc-code'> · {c.tradeName}</span>}
        </>
      ),
    },
    {
      key: 'document',
      header: 'Documento',
      role: 'meta',
      cell: (c) => (
        <span className='t-mono'>
          {c.personType === PersonType.Company ? maskCnpj(c.document) : maskCpf(c.document)}
        </span>
      ),
    },
    {
      key: 'personType',
      header: 'Tipo',
      role: 'meta',
      cell: (c) => (c.personType === PersonType.Company ? 'PJ' : 'PF'),
    },
    {
      key: 'creditLimit',
      header: 'Limite de crédito',
      role: 'amount',
      cell: (c) => <span className='t-mono'>{c.creditLimit != null ? brl(c.creditLimit) : '—'}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      role: 'status',
      cell: (c) => (
        <span className={`badge-status ${c.active ? 'badge-pago' : 'badge-rejeitado'}`}>
          {c.active ? 'Ativo' : 'Inativo'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (c) => {
        if (!canWrite) return null;
        return (
          <>
            <button className='btn btn-icon' onClick={() => onEdit(c)} aria-label='Editar' title='Editar'>
              <Pencil />
            </button>
            <button
              className='btn btn-icon'
              onClick={() => onToggle(c)}
              aria-label={c.active ? 'Desativar' : 'Ativar'}
              title={c.active ? 'Desativar' : 'Ativar'}
            >
              <Power />
            </button>
            <button className='btn btn-icon' onClick={() => onDelete(c)} aria-label='Excluir' title='Excluir'>
              <Trash2 />
            </button>
          </>
        );
      },
    },
  ];

  return <ResponsiveTable rows={customers} columns={columns} rowKey={(c) => c.id} onRowClick={(c) => onEdit(c)} />;
}
