import { useEffect, useState } from 'react';
import { Plus, Search, Users, Power, Trash2, Pencil } from 'lucide-react';
import { type Employee } from '../api';
import { getErrorMessage } from '../apiError';
import { useApp } from '../store';
import { useCostCenters, useEmployeeAction, useEmployees, useUnits } from '../queries';
import { Button, Drawer, ConfirmDialog, EmptyState, PageHeader, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { maskCpf } from '../format';
import { EmpresaPicker } from './shared';
import { EmployeeForm } from './registry-forms';

export function ColaboradoresPage() {
  const { empresa, empresaId } = useApp();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [drawer, setDrawer] = useState<null | { edit?: Employee }>(null);
  const [confirm, setConfirm] = useState<null | { employee: Employee; action: 'toggle' | 'delete' }>(null);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(t);
  }, [search]);

  const list = useEmployees(empresaId, debouncedSearch);
  const unitsQuery = useUnits(empresaId);
  const costCentersQuery = useCostCenters(empresaId);
  const units = unitsQuery.data ?? [];
  const costCenters = costCentersQuery.data ?? [];

  const employees = list.data ?? [];
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
        <EmpresaPicker />
        <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => setDrawer({})}>
          Novo colaborador
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Users />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : (
        <Panel
          title={`${employees.length} colaborador(es)`}
          action={
            <div className='top-search' style={{ width: 260 }}>
              <Search />
              <input placeholder='Buscar por nome ou CPF…' value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
          }
        >
          <table>
            <thead>
              <tr>
                <th>Nome</th>
                <th>CPF</th>
                <th>Cargo</th>
                <th>Unidade</th>
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
              ) : employees.length === 0 ? (
                <tr>
                  <td colSpan={6} className='t-empty'>
                    Nenhum colaborador encontrado.
                  </td>
                </tr>
              ) : (
                employees.map((emp) => (
                  <tr key={emp.id} className='row-click' onClick={() => setDrawer({ edit: emp })}>
                    <td>{emp.name}</td>
                    <td className='t-mono'>{maskCpf(emp.document)}</td>
                    <td>{emp.role}</td>
                    <td>{unitName(emp.unitId)}</td>
                    <td>
                      <span className={`badge-status ${emp.active ? 'badge-pago' : 'badge-rejeitado'}`}>
                        {emp.active ? 'Ativo' : 'Inativo'}
                      </span>
                    </td>
                    <td className='td-action' onClick={(e) => e.stopPropagation()}>
                      <button className='icon-btn' onClick={() => setDrawer({ edit: emp })} aria-label='Editar'>
                        <Pencil />
                      </button>
                      <button
                        className='icon-btn'
                        onClick={() => setConfirm({ employee: emp, action: 'toggle' })}
                        aria-label={emp.active ? 'Desativar' : 'Ativar'}
                      >
                        <Power />
                      </button>
                      <button
                        className='icon-btn'
                        onClick={() => setConfirm({ employee: emp, action: 'delete' })}
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
