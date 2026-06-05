import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Plus, Pencil } from 'lucide-react';
import { type Company, type Unit } from '../api';
import { useApp } from '../store';
import { useCreateCompany, useCreateUnit, useUnits, useUpdateCompany, useUpdateUnit } from '../queries';
import { getErrorMessage } from '../apiError';
import { Button, Input, PageHeader, Panel } from '../ui';
import { EmpresaPicker } from './shared';
import { resolver, companySchema, unitSchema, type CompanyValues, type UnitValues } from './schemas';

export function EstruturaPage() {
  const { empresas, empresa, empresaId, refreshEmpresas } = useApp();
  const unidades = useUnits(empresaId).data ?? [];
  const [empresaForm, setEmpresaForm] = useState<null | { edit?: Company }>(null);
  const [unidadeForm, setUnidadeForm] = useState<null | { edit?: Unit }>(null);

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
            <Button variant='brand' icon={<Plus />} onClick={() => setEmpresaForm({})}>
              Nova empresa
            </Button>
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
                    <td>{e.active ? 'Ativa' : 'Inativa'}</td>
                    <td className='td-action'>
                      <button className='icon-btn' onClick={() => setEmpresaForm({ edit: e })} aria-label='Editar'>
                        <Pencil />
                      </button>
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
            <EmpresaPicker />
            <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => setUnidadeForm({})}>
              Nova unidade
            </Button>
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
              <th>Nome</th>
              <th>Sigla</th>
              <th>Status</th>
              <th className='td-action'></th>
            </tr>
          </thead>
          <tbody>
            {!empresa ? (
              <tr>
                <td colSpan={4} className='t-empty'>
                  Selecione uma empresa.
                </td>
              </tr>
            ) : unidades.length === 0 ? (
              <tr>
                <td colSpan={4} className='t-empty'>
                  Nenhuma unidade nesta empresa.
                </td>
              </tr>
            ) : (
              unidades.map((u) => (
                <tr key={u.id}>
                  <td>{u.name}</td>
                  <td className='t-mono'>{u.acronym}</td>
                  <td>{u.active ? 'Ativa' : 'Inativa'}</td>
                  <td className='td-action'>
                    <button className='icon-btn' onClick={() => setUnidadeForm({ edit: u })} aria-label='Editar'>
                      <Pencil />
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </Panel>
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
    defaultValues: { name: initial?.name ?? '', acronym: initial?.acronym ?? '' },
  });
  const create = useCreateUnit({ onSuccess: onSaved });
  const update = useUpdateUnit({ onSuccess: onSaved });
  const mutation = initial ? update : create;
  const salvando = mutation.isPending;
  const erro = mutation.error ? getErrorMessage(mutation.error) : null;
  const onValid = (v: UnitValues) =>
    initial
      ? update.mutate({ id: initial.id, name: v.name.trim(), acronym: v.acronym.trim() })
      : create.mutate({ companyId: empresa.id, name: v.name.trim(), acronym: v.acronym.trim() });
  return (
    <form className='inline-form' onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <div className='grow'>
        <Input autoFocus placeholder='Nome da unidade' {...register('name')} />
      </div>
      <Input placeholder='Sigla' {...register('acronym')} />
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
