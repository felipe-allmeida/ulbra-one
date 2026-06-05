import { useEffect, useState } from 'react';
import { Plus, Search, Users, Power, Trash2, Pencil } from 'lucide-react';
import { api, type Employee, type Unit, type CostCenter } from '../api';
import { getErrorMessage } from '../apiError';
import { useApp } from '../store';
import { Button, Drawer, ConfirmDialog, EmptyState, PageHeader, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { useAsync } from '../useAsync';
import { maskCpf } from '../format';
import { EmpresaPicker } from './shared';
import { EmployeeForm } from './registry-forms';

export function ColaboradoresPage() {
  const { empresa, empresaId } = useApp();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [units, setUnits] = useState<Unit[]>([]);
  const [costCenters, setCostCenters] = useState<CostCenter[]>([]);
  const [drawer, setDrawer] = useState<null | { edit?: Employee }>(null);
  const [confirm, setConfirm] = useState<null | { employee: Employee; action: 'toggle' | 'delete' }>(null);
  const [busy, setBusy] = useState(false);

  const list = useAsync<Employee[]>(() => api.listEmployees(empresaId, search));
  const reload = () => {
    if (empresaId) list.run();
  };
  useEffect(() => {
    if (!empresaId) return;
    const t = setTimeout(reload, 250);
    return () => clearTimeout(t);
  }, [empresaId, search]);
  useEffect(() => {
    if (!empresaId) {
      setUnits([]);
      setCostCenters([]);
      return;
    }
    Promise.all([api.listUnits(empresaId), api.listCostCenters(empresaId)])
      .then(([u, c]) => {
        setUnits(u);
        setCostCenters(c);
      })
      .catch(() => toast.error('Falha ao carregar unidades/centros de custo.'));
  }, [empresaId]);

  const employees = list.data ?? [];
  const unitName = (id: string) => units.find((u) => u.id === id)?.name ?? '—';

  const onConfirm = async () => {
    if (!confirm) return;
    setBusy(true);
    try {
      const { employee, action } = confirm;
      if (action === 'delete') {
        await api.removeEmployee(employee.id);
        toast.success('Colaborador excluído.');
      } else if (employee.active) {
        await api.deactivateEmployee(employee.id);
        toast.success('Colaborador desativado.');
      } else {
        await api.activateEmployee(employee.id);
        toast.success('Colaborador ativado.');
      }
      setConfirm(null);
      reload();
    } catch (e) {
      toast.error(getErrorMessage(e));
    } finally {
      setBusy(false);
    }
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
          busy={busy}
          confirmLabel={confirm.action === 'delete' ? 'Excluir' : confirm.employee.active ? 'Desativar' : 'Ativar'}
          onConfirm={onConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}
