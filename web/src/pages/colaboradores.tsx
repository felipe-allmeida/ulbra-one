import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Search, Users, Power, Trash2, Pencil } from 'lucide-react';
import { type Employee } from '../api';
import { getErrorMessage } from '../apiError';
import { useApp } from '../store';
import { useCostCenters, useEmployeeAction, useEmployees, useUnits } from '../queries';
import { Button, Drawer, ConfirmDialog, EmptyState, PageHeader, Pager, Panel, LoadingState } from '../ui';
import { useToast } from '../toast';
import { P, usePermissions } from '../permissions';
import { maskCpf } from '../format';
import { EmployeeForm } from './registry-forms';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';

export function ColaboradoresPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const toast = useToast();
  const canWrite = usePermissions().can(P.registryWrite);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [page, setPage] = useState(1);
  const [drawer, setDrawer] = useState<null | { edit?: Employee }>(null);
  const [confirm, setConfirm] = useState<null | { employee: Employee; action: 'toggle' | 'delete' }>(null);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(t);
  }, [search]);

  // Any change to the company or active search resets paging to the first page.
  useEffect(() => setPage(1), [empresaId, debouncedSearch]);

  const list = useEmployees(empresaId, debouncedSearch, page);
  const unitsQuery = useUnits(empresaId);
  const costCentersQuery = useCostCenters(empresaId);
  const units = unitsQuery.data ?? [];
  const costCenters = costCentersQuery.data ?? [];

  const employees = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;
  const total = list.data?.total ?? 0;
  const unitName = (id: string) => units.find((u) => u.id === id)?.name ?? '—';

  const action = useEmployeeAction({
    onSuccess: (_data, vars) => {
      toast.success(
        vars.action === 'delete'
          ? 'Colaborador excluído.'
          : vars.action === 'deactivate'
            ? 'Colaborador desativado.'
            : 'Colaborador ativado.',
      );
      setConfirm(null);
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const onConfirm = () => {
    if (!confirm) return;
    const { employee, action: act } = confirm;
    const resolved = act === 'delete' ? 'delete' : employee.active ? 'deactivate' : 'activate';
    action.mutate({ id: employee.id, action: resolved });
  };

  return (
    <>
      <PageHeader title='Colaboradores' sub='Pessoas físicas vinculadas a uma unidade do grupo.'>
        {canWrite && (
          <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => setDrawer({})}>
            Novo colaborador
          </Button>
        )}
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Users />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : (
        <Panel
          title={`${total} colaborador(es)`}
          action={
            <div className='top-search' style={{ width: 260 }}>
              <Search />
              <input placeholder='Buscar por nome ou CPF…' value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
          }
        >
          {list.isLoading ? (
            <LoadingState />
          ) : list.error ? (
            <EmptyState
              icon={<Users />}
              title={t('common.loadError')}
              sub={getErrorMessage(list.error)}
              action={
                <button className='backlink' onClick={() => void list.refetch()}>
                  {t('common.retry')}
                </button>
              }
            />
          ) : employees.length === 0 ? (
            <table>
              <tbody>
                <tr>
                  <td className='t-empty'>Nenhum colaborador encontrado.</td>
                </tr>
              </tbody>
            </table>
          ) : (
            <EmployeesTable
              employees={employees}
              unitName={unitName}
              canWrite={canWrite}
              onEdit={(emp) => setDrawer({ edit: emp })}
              onToggle={(emp) => setConfirm({ employee: emp, action: 'toggle' })}
              onDelete={(emp) => setConfirm({ employee: emp, action: 'delete' })}
            />
          )}
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </Panel>
      )}

      <Drawer
        open={!!drawer}
        title={drawer?.edit ? 'Editar colaborador' : 'Novo colaborador'}
        sub={empresa?.legalName}
        onClose={() => setDrawer(null)}
      >
        {drawer && empresa && (
          <EmployeeForm
            companyId={empresa.id}
            units={units}
            costCenters={costCenters}
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
              ? 'Excluir colaborador'
              : confirm.employee.active
                ? 'Desativar colaborador'
                : 'Ativar colaborador'
          }
          message={
            confirm.action === 'delete' ? (
              <>
                Excluir <b>{confirm.employee.name}</b>? Esta ação remove o cadastro (soft-delete).
              </>
            ) : confirm.employee.active ? (
              <>
                Desativar <b>{confirm.employee.name}</b>?
              </>
            ) : (
              <>
                Reativar <b>{confirm.employee.name}</b>?
              </>
            )
          }
          danger={confirm.action === 'delete'}
          busy={action.isPending}
          confirmLabel={confirm.action === 'delete' ? 'Excluir' : confirm.employee.active ? 'Desativar' : 'Ativar'}
          onConfirm={onConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}

/**
 * Ordem = nome/cpf/cargo/unidade/status/ações, igual a hoje. `nome` é o `title`; `cpf` e `cargo`
 * são os dois `meta` (identificam de relance); `unidade` fica em `detail` (orçamento de meta já
 * usado); `status` é badge; `ações` (editar/ativar-desativar/excluir) ganha `align: 'right'` — o `<td>` original levava `.td-action` (width:1%, sem quebra, alinhado à
 * direita) e `header: ''` porque o `<th>` original era vazio.
 */
function EmployeesTable({
  employees,
  unitName,
  canWrite,
  onEdit,
  onToggle,
  onDelete,
}: {
  employees: Employee[];
  unitName: (id: string) => string;
  canWrite: boolean;
  onEdit: (emp: Employee) => void;
  onToggle: (emp: Employee) => void;
  onDelete: (emp: Employee) => void;
}) {
  const columns: Column<Employee>[] = [
    { key: 'name', header: 'Nome', role: 'title', cell: (emp) => emp.name },
    {
      key: 'document',
      header: 'CPF',
      role: 'meta',
      cell: (emp) => <span className='t-mono'>{maskCpf(emp.document)}</span>,
    },
    { key: 'role', header: 'Cargo', role: 'meta', cell: (emp) => emp.role },
    { key: 'unit', header: 'Unidade', role: 'detail', cell: (emp) => unitName(emp.unitId) },
    {
      key: 'status',
      header: 'Status',
      role: 'status',
      cell: (emp) => (
        <span className={`badge-status ${emp.active ? 'badge-pago' : 'badge-rejeitado'}`}>
          {emp.active ? 'Ativo' : 'Inativo'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (emp) => {
        if (!canWrite) return null;
        return (
          <>
            <button className='btn btn-icon' onClick={() => onEdit(emp)} aria-label='Editar' title='Editar'>
              <Pencil />
            </button>
            <button
              className='btn btn-icon'
              onClick={() => onToggle(emp)}
              aria-label={emp.active ? 'Desativar' : 'Ativar'}
              title={emp.active ? 'Desativar' : 'Ativar'}
            >
              <Power />
            </button>
            <button className='btn btn-icon' onClick={() => onDelete(emp)} aria-label='Excluir' title='Excluir'>
              <Trash2 />
            </button>
          </>
        );
      },
    },
  ];

  return (
    <ResponsiveTable rows={employees} columns={columns} rowKey={(emp) => emp.id} onRowClick={(emp) => onEdit(emp)} />
  );
}
