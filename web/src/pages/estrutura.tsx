import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Plus, Pencil, Power, Trash2 } from 'lucide-react';
import { type Company, type Unit } from '../api';
import { maskCnpj } from '../format';
import { useApp } from '../store';
import {
  useCompanyAction,
  useCreateCompany,
  useCreateUnit,
  useUnits,
  useUpdateCompany,
  useUpdateUnit,
} from '../queries';
import { getErrorMessage } from '../apiError';
import { Button, ConfirmDialog, Input, LoadingState, PageHeader, Panel } from '../ui';
import { useToast } from '../toast';
import { P, usePermissions } from '../permissions';
import { resolver, companySchema, unitSchema, type CompanyValues, type UnitValues } from './schemas';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';

export function EstruturaPage() {
  const { empresas, empresa, empresaId, isAllCompanies, scopeReady, refreshEmpresas } = useApp();
  const perms = usePermissions();
  const toast = useToast();
  // Consolidated "Todas" (AEL-906): read-only; write actions hidden; unit rows show their company.
  const canWrite = perms.can(P.coreWrite) && !isAllCompanies;
  // Delete is admin-only (soft-delete); inactivate/reactivate ride on core:write like edit.
  const canDelete = perms.isAdmin && !isAllCompanies;
  const unitsQuery = useUnits(empresaId);
  const unidades = unitsQuery.data ?? [];
  const nomeEmpresa = (id: string) => empresas.find((e) => e.id === id)?.legalName ?? '—';
  const [empresaForm, setEmpresaForm] = useState<null | { edit?: Company }>(null);
  const [unidadeForm, setUnidadeForm] = useState<null | { edit?: Unit }>(null);
  const [confirm, setConfirm] = useState<null | { company: Company; action: 'toggle' | 'delete' }>(null);

  const companyAction = useCompanyAction({
    onSuccess: async (_data, vars) => {
      // Refresh the store list so a deleted company also drops from the topbar selector (re-selects if it was current).
      await refreshEmpresas();
      toast.success(
        vars.action === 'delete'
          ? 'Empresa excluída.'
          : vars.action === 'deactivate'
            ? 'Empresa inativada.'
            : 'Empresa reativada.',
      );
      setConfirm(null);
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const onConfirm = () => {
    if (!confirm) return;
    const { company, action } = confirm;
    const resolved = action === 'delete' ? 'delete' : company.active ? 'deactivate' : 'activate';
    companyAction.mutate({ id: company.id, action: resolved });
  };

  return (
    <>
      <PageHeader
        title='Empresas e unidades'
        sub='Pessoas jurídicas (CNPJ) do grupo e suas unidades (campus, polos).'
      />

      <div style={{ marginBottom: 16 }}>
        <Panel
          title='Empresas'
          action={
            canWrite ? (
              <Button variant='brand' icon={<Plus />} onClick={() => setEmpresaForm({})}>
                Nova empresa
              </Button>
            ) : undefined
          }
        >
          {empresaForm && (
            <InlineEmpresaForm
              initial={empresaForm.edit}
              onClose={() => setEmpresaForm(null)}
              onSaved={async (e) => {
                await refreshEmpresas(e.id);
                setEmpresaForm(null);
              }}
            />
          )}
          {empresas.length === 0 ? (
            <table>
              <tbody>
                <tr>
                  <td className='t-empty'>Nenhuma empresa cadastrada.</td>
                </tr>
              </tbody>
            </table>
          ) : (
            <EmpresasTable
              empresas={empresas}
              canWrite={canWrite}
              canDelete={canDelete}
              onEdit={(e) => setEmpresaForm({ edit: e })}
              onToggle={(e) => setConfirm({ company: e, action: 'toggle' })}
              onDelete={(e) => setConfirm({ company: e, action: 'delete' })}
            />
          )}
        </Panel>
      </div>

      <Panel
        title='Unidades'
        action={
          <div className='toolbar'>
            {canWrite && (
              <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => setUnidadeForm({})}>
                Nova unidade
              </Button>
            )}
          </div>
        }
      >
        {unidadeForm && empresa && (
          <InlineUnidadeForm
            empresa={empresa}
            initial={unidadeForm.edit}
            onClose={() => setUnidadeForm(null)}
            onSaved={() => setUnidadeForm(null)}
          />
        )}
        {!scopeReady ? (
          <table>
            <tbody>
              <tr>
                <td className='t-empty'>Selecione uma empresa.</td>
              </tr>
            </tbody>
          </table>
        ) : unitsQuery.isLoading ? (
          <LoadingState />
        ) : unidades.length === 0 ? (
          <table>
            <tbody>
              <tr>
                <td className='t-empty'>
                  {isAllCompanies ? 'Nenhuma unidade cadastrada.' : 'Nenhuma unidade nesta empresa.'}
                </td>
              </tr>
            </tbody>
          </table>
        ) : (
          <UnidadesTable
            unidades={unidades}
            isAllCompanies={isAllCompanies}
            nomeEmpresa={nomeEmpresa}
            canWrite={canWrite}
            onEdit={(u) => setUnidadeForm({ edit: u })}
          />
        )}
      </Panel>

      {confirm && (
        <ConfirmDialog
          title={
            confirm.action === 'delete'
              ? 'Excluir empresa'
              : confirm.company.active
                ? 'Inativar empresa'
                : 'Reativar empresa'
          }
          message={
            confirm.action === 'delete' ? (
              <>
                Excluir <b>{confirm.company.legalName}</b>? A empresa sai da lista e do seletor (soft-delete). As
                unidades e o histórico permanecem no banco.
              </>
            ) : confirm.company.active ? (
              <>
                Inativar <b>{confirm.company.legalName}</b>? Ela continua na lista, marcada como inativa.
              </>
            ) : (
              <>
                Reativar <b>{confirm.company.legalName}</b>?
              </>
            )
          }
          danger={confirm.action === 'delete'}
          busy={companyAction.isPending}
          confirmLabel={confirm.action === 'delete' ? 'Excluir' : confirm.company.active ? 'Inativar' : 'Reativar'}
          onConfirm={onConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}

/**
 * Ordem = razão social/cnpj/status/ações, igual a hoje. `razão social` é o `title`; `cnpj` é o
 * único `meta`; `status` é badge; `ações` (editar/inativar-reativar/excluir) ganha `align: 'right'` — o `<td>` original levava `.td-action` (width:1%, sem quebra, alinhado à
 * direita) e `header: ''` porque o `<th>` original era vazio. Sem `onRowClick` — a linha nunca
 * navegou.
 */
function EmpresasTable({
  empresas,
  canWrite,
  canDelete,
  onEdit,
  onToggle,
  onDelete,
}: {
  empresas: Company[];
  canWrite: boolean;
  canDelete: boolean;
  onEdit: (e: Company) => void;
  onToggle: (e: Company) => void;
  onDelete: (e: Company) => void;
}) {
  const columns: Column<Company>[] = [
    { key: 'legalName', header: 'Razão social', role: 'title', cell: (e) => e.legalName },
    { key: 'cnpj', header: 'CNPJ', role: 'meta', cell: (e) => <span className='t-mono'>{e.cnpj}</span> },
    {
      key: 'status',
      header: 'Status',
      role: 'status',
      cell: (e) => (
        <span className={`badge-status ${e.active ? 'badge-pago' : 'badge-rejeitado'}`}>
          {e.active ? 'Ativa' : 'Inativa'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (e) => (
        <>
          {canWrite && (
            <>
              <button className='btn btn-icon' onClick={() => onEdit(e)} aria-label='Editar' title='Editar'>
                <Pencil />
              </button>
              <button
                className='btn btn-icon'
                onClick={() => onToggle(e)}
                aria-label={e.active ? 'Inativar' : 'Reativar'}
                title={e.active ? 'Inativar' : 'Reativar'}
              >
                <Power />
              </button>
            </>
          )}
          {canDelete && (
            <button className='btn btn-icon' onClick={() => onDelete(e)} aria-label='Excluir' title='Excluir'>
              <Trash2 />
            </button>
          )}
        </>
      ),
    },
  ];

  return <ResponsiveTable rows={empresas} columns={columns} rowKey={(e) => e.id} />;
}

/**
 * Ordem = [empresa?]/nome/sigla/cnpj/status/ações, igual a hoje. `nome` (+ badge "Matriz") é o
 * `title`; `sigla` é sempre `meta` — no modo "Todas as empresas" `empresa` some o segundo `meta`
 * (identifica a unidade entre empresas) e `cnpj` cede para `detail` (orçamento de meta já usado);
 * fora desse modo `cnpj` volta a ser `meta` (não há coluna `empresa` disputando o espaço). `status`
 * é texto simples (não badge, igual a hoje — role só controla o layout, não o estilo); `ações`
 * (editar) ganha `align: 'right'` — o `<td>` original levava `.td-action`.
 */
function UnidadesTable({
  unidades,
  isAllCompanies,
  nomeEmpresa,
  canWrite,
  onEdit,
}: {
  unidades: Unit[];
  isAllCompanies: boolean;
  nomeEmpresa: (id: string) => string;
  canWrite: boolean;
  onEdit: (u: Unit) => void;
}) {
  const columns: Column<Unit>[] = [
    ...(isAllCompanies
      ? [{ key: 'company', header: 'Empresa', role: 'meta' as const, cell: (u: Unit) => nomeEmpresa(u.companyId) }]
      : []),
    {
      key: 'name',
      header: 'Nome',
      role: 'title',
      cell: (u) => (
        <>
          {u.name}
          {u.isMatriz && (
            <span
              style={{
                marginLeft: 8,
                fontSize: 11,
                fontWeight: 600,
                padding: '1px 6px',
                borderRadius: 4,
                background: 'var(--brand-subtle)',
                color: 'var(--brand)',
              }}
            >
              Matriz
            </span>
          )}
        </>
      ),
    },
    { key: 'acronym', header: 'Sigla', role: 'meta', cell: (u) => <span className='t-mono'>{u.acronym}</span> },
    {
      key: 'cnpj',
      header: 'CNPJ',
      role: isAllCompanies ? 'detail' : 'meta',
      cell: (u) => <span className='t-mono'>{u.cnpj ? maskCnpj(u.cnpj) : '—'}</span>,
    },
    { key: 'status', header: 'Status', role: 'status', cell: (u) => (u.active ? 'Ativa' : 'Inativa') },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (u) =>
        canWrite && (
          <button className='btn btn-icon' onClick={() => onEdit(u)} aria-label='Editar' title='Editar'>
            <Pencil />
          </button>
        ),
    },
  ];

  return <ResponsiveTable rows={unidades} columns={columns} rowKey={(u) => u.id} />;
}

function InlineEmpresaForm({
  initial,
  onClose,
  onSaved,
}: {
  initial?: Company;
  onClose: () => void;
  onSaved: (e: Company) => void;
}) {
  const { register, handleSubmit, formState } = useForm<CompanyValues>({
    resolver: resolver(companySchema),
    mode: 'onChange',
    defaultValues: { legalName: initial?.legalName ?? '', cnpj: initial?.cnpj ?? '' },
  });
  const create = useCreateCompany({ onSuccess: onSaved });
  const update = useUpdateCompany({ onSuccess: onSaved });
  const mutation = initial ? update : create;
  const salvando = mutation.isPending;
  const erro = mutation.error ? getErrorMessage(mutation.error) : null;
  const onValid = (v: CompanyValues) =>
    initial
      ? update.mutate({ id: initial.id, legalName: v.legalName.trim(), cnpj: v.cnpj.trim() })
      : create.mutate({ legalName: v.legalName.trim(), cnpj: v.cnpj.trim() });
  return (
    <form className='inline-form' onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <div className='grow'>
        <Input autoFocus placeholder='Razão social' {...register('legalName')} />
      </div>
      <Input placeholder='CNPJ (14 dígitos)' {...register('cnpj')} />
      <Button type='submit' variant='brand' disabled={!formState.isValid || salvando}>
        {initial ? 'Salvar' : 'Adicionar'}
      </Button>
      <Button type='button' onClick={onClose}>
        Cancelar
      </Button>
      {erro && (
        <div className='create-err' style={{ flexBasis: '100%' }}>
          {erro}
        </div>
      )}
    </form>
  );
}

function InlineUnidadeForm({
  empresa,
  initial,
  onClose,
  onSaved,
}: {
  empresa: Company;
  initial?: Unit;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { register, handleSubmit, formState } = useForm<UnitValues>({
    resolver: resolver(unitSchema),
    mode: 'onChange',
    defaultValues: {
      name: initial?.name ?? '',
      acronym: initial?.acronym ?? '',
      cnpj: initial?.cnpj ?? '',
      isMatriz: initial?.isMatriz ?? false,
    },
  });
  const create = useCreateUnit({ onSuccess: onSaved });
  const update = useUpdateUnit({ onSuccess: onSaved });
  const mutation = initial ? update : create;
  const salvando = mutation.isPending;
  const erro = mutation.error ? getErrorMessage(mutation.error) : null;
  const onValid = (v: UnitValues) => {
    const cnpj = v.cnpj?.trim() ? v.cnpj.trim() : undefined;
    const isMatriz = v.isMatriz ?? false;
    return initial
      ? update.mutate({ id: initial.id, name: v.name.trim(), acronym: v.acronym.trim(), cnpj, isMatriz })
      : create.mutate({ companyId: empresa.id, name: v.name.trim(), acronym: v.acronym.trim(), cnpj, isMatriz });
  };
  return (
    <form className='inline-form' onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <div className='grow'>
        <Input autoFocus placeholder='Nome da unidade' {...register('name')} />
      </div>
      <Input placeholder='Sigla' {...register('acronym')} />
      <Input placeholder='CNPJ (opcional)' {...register('cnpj')} />
      <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 13, whiteSpace: 'nowrap' }}>
        <input type='checkbox' {...register('isMatriz')} /> Matriz
      </label>
      <Button type='submit' variant='brand' disabled={!formState.isValid || salvando}>
        {initial ? 'Salvar' : 'Adicionar'}
      </Button>
      <Button type='button' onClick={onClose}>
        Cancelar
      </Button>
      {erro && (
        <div className='create-err' style={{ flexBasis: '100%' }}>
          {erro}
        </div>
      )}
    </form>
  );
}
