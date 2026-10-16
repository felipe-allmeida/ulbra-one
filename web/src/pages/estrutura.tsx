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
import { Button, ConfirmDialog, Input, PageHeader, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { P, usePermissions } from '../permissions';
import { resolver, companySchema, unitSchema, type CompanyValues, type UnitValues } from './schemas';

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
          <table>
            <thead>
              <tr>
                <th>Razão social</th>
                <th>CNPJ</th>
                <th>Status</th>
                <th className='td-action'></th>
              </tr>
            </thead>
            <tbody>
              {empresas.length === 0 ? (
                <tr>
                  <td colSpan={4} className='t-empty'>
                    Nenhuma empresa cadastrada.
                  </td>
                </tr>
              ) : (
                empresas.map((e) => (
                  <tr key={e.id}>
                    <td>{e.legalName}</td>
                    <td className='t-mono'>{e.cnpj}</td>
                    <td>
                      <span className={`badge-status ${e.active ? 'badge-pago' : 'badge-rejeitado'}`}>
                        {e.active ? 'Ativa' : 'Inativa'}
                      </span>
                    </td>
                    <td className='td-action'>
                      {canWrite && (
                        <>
                          <button className='icon-btn' onClick={() => setEmpresaForm({ edit: e })} aria-label='Editar'>
                            <Pencil />
                          </button>
                          <button
                            className='icon-btn'
                            onClick={() => setConfirm({ company: e, action: 'toggle' })}
                            aria-label={e.active ? 'Inativar' : 'Reativar'}
                          >
                            <Power />
                          </button>
                        </>
                      )}
                      {canDelete && (
                        <button
                          className='icon-btn'
                          onClick={() => setConfirm({ company: e, action: 'delete' })}
                          aria-label='Excluir'
                        >
                          <Trash2 />
                        </button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
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
        <table>
          <thead>
            <tr>
              {isAllCompanies && <th>Empresa</th>}
              <th>Nome</th>
              <th>Sigla</th>
              <th>CNPJ</th>
              <th>Status</th>
              <th className='td-action'></th>
            </tr>
          </thead>
          <tbody>
            {!scopeReady ? (
              <tr>
                <td colSpan={isAllCompanies ? 6 : 5} className='t-empty'>
                  Selecione uma empresa.
                </td>
              </tr>
            ) : unitsQuery.isLoading ? (
              <SkeletonRows rows={4} cols={isAllCompanies ? 6 : 5} />
            ) : unidades.length === 0 ? (
              <tr>
                <td colSpan={isAllCompanies ? 6 : 5} className='t-empty'>
                  {isAllCompanies ? 'Nenhuma unidade cadastrada.' : 'Nenhuma unidade nesta empresa.'}
                </td>
              </tr>
            ) : (
              unidades.map((u) => (
                <tr key={u.id}>
                  {isAllCompanies && <td>{nomeEmpresa(u.companyId)}</td>}
                  <td>
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
                  </td>
                  <td className='t-mono'>{u.acronym}</td>
                  <td className='t-mono'>{u.cnpj ? maskCnpj(u.cnpj) : '—'}</td>
                  <td>{u.active ? 'Ativa' : 'Inativa'}</td>
                  <td className='td-action'>
                    {canWrite && (
                      <button className='icon-btn' onClick={() => setUnidadeForm({ edit: u })} aria-label='Editar'>
                        <Pencil />
                      </button>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
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
