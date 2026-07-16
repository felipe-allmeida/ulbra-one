import { Controller, useForm } from 'react-hook-form';
import { type Company, type Unit, type CostCenter } from '../api';
import { useApp } from '../store';
import { useSaveCostCenter } from '../queries';
import { getErrorMessage } from '../apiError';
import { P, usePermissions } from '../permissions';
import { Button, Input, Select, Field, ToolbarField } from '../ui';
import { resolver, fieldError, costCenterSchema, type CostCenterValues } from './schemas';

export function EmpresaPicker() {
  const { empresas, empresaId, setEmpresaId } = useApp();
  return (
    <ToolbarField label='Empresa'>
      <Select value={empresaId} onChange={(e) => setEmpresaId(e.target.value)}>
        {empresas.length === 0 && <option value=''>— nenhuma —</option>}
        {empresas.map((e) => (
          <option key={e.id} value={e.id}>
            {e.legalName}
          </option>
        ))}
      </Select>
    </ToolbarField>
  );
}

export function UnidadePicker({
  unidades,
  sel,
  setSel,
}: {
  unidades: Unit[];
  sel: Set<string>;
  setSel: (s: Set<string>) => void;
}) {
  const todas = unidades.length > 0 && sel.size === unidades.length;
  const toggle = (id: string) => {
    const n = new Set(sel);
    n.has(id) ? n.delete(id) : n.add(id);
    setSel(n);
  };
  if (unidades.length === 0) return <div className='uni-pick-empty'>Cadastre uma unidade primeiro (em Estrutura).</div>;
  return (
    <div className='uni-pick'>
      <label className='uni-pick-row' style={{ fontWeight: 600 }}>
        <input
          type='checkbox'
          checked={todas}
          onChange={() => setSel(todas ? new Set() : new Set(unidades.map((u) => u.id)))}
        />{' '}
        Todas as unidades
      </label>
      <div className='uni-pick-sep' />
      {unidades.map((u) => (
        <label className='uni-pick-row' key={u.id}>
          <input type='checkbox' checked={sel.has(u.id)} onChange={() => toggle(u.id)} /> {u.name}{' '}
          <span className='sig'>{u.acronym}</span>
        </label>
      ))}
    </div>
  );
}

/** Cost center form (create or edit). */
export function CentroForm({
  empresa,
  unidades,
  initial,
  onSaved,
  onCancel,
}: {
  empresa: Company;
  unidades: Unit[];
  initial?: CostCenter;
  onSaved: (cc: CostCenter) => void;
  onCancel: () => void;
}) {
  const canWrite = usePermissions().can(P.coreWrite);
  const { control, handleSubmit, formState } = useForm<CostCenterValues>({
    resolver: resolver(costCenterSchema),
    mode: 'onChange',
    defaultValues: {
      code: initial?.code ?? '',
      name: initial?.name ?? '',
      annualBudget: initial ? String(initial.annualBudget) : '',
      autoApprovalLimit: initial ? String(initial.autoApprovalLimit) : '',
      unitIds: initial?.unitIds ?? [],
    },
  });

  const mutation = useSaveCostCenter({ onSuccess: (cc) => onSaved(cc) });
  const salvando = mutation.isPending;
  const erro = mutation.error ? getErrorMessage(mutation.error) : null;
  const onValid = (v: CostCenterValues) =>
    mutation.mutate({
      id: initial?.id,
      companyId: empresa.id,
      code: v.code.trim(),
      name: v.name.trim(),
      annualBudget: Number(v.annualBudget) || 0,
      autoApprovalLimit: Number(v.autoApprovalLimit) || 0,
      unitIds: v.unitIds,
    });

  return (
    <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <div className='edit-grid'>
        <Field label='Código'>
          <Controller
            control={control}
            name='code'
            render={({ field }) => <Input autoFocus {...field} placeholder='ex.: MKT' />}
          />
          {fieldError(formState.errors.code?.message) && (
            <div className='field-err'>{fieldError(formState.errors.code?.message)}</div>
          )}
        </Field>
        <Field label='Nome'>
          <Controller
            control={control}
            name='name'
            render={({ field }) => <Input {...field} placeholder='ex.: Marketing' />}
          />
          {fieldError(formState.errors.name?.message) && (
            <div className='field-err'>{fieldError(formState.errors.name?.message)}</div>
          )}
        </Field>
        <Field label='Orçamento anual (R$)'>
          <Controller
            control={control}
            name='annualBudget'
            render={({ field }) => <Input {...field} inputMode='numeric' placeholder='ex.: 500000' />}
          />
          {fieldError(formState.errors.annualBudget?.message) && (
            <div className='field-err'>{fieldError(formState.errors.annualBudget?.message)}</div>
          )}
        </Field>
        <Field
          label='Limite de auto-aprovação (R$)'
          help='Despesas até este valor são aprovadas automaticamente. 0 = sem auto-aprovação.'
        >
          <Controller
            control={control}
            name='autoApprovalLimit'
            render={({ field }) => <Input {...field} inputMode='numeric' placeholder='ex.: 5000' />}
          />
          {fieldError(formState.errors.autoApprovalLimit?.message) && (
            <div className='field-err'>{fieldError(formState.errors.autoApprovalLimit?.message)}</div>
          )}
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Field label='Atua nas unidades'>
          <Controller
            control={control}
            name='unitIds'
            render={({ field }) => (
              <UnidadePicker unidades={unidades} sel={new Set(field.value)} setSel={(s) => field.onChange([...s])} />
            )}
          />
        </Field>
      </div>
      {erro && (
        <div className='create-err' style={{ marginTop: 10 }}>
          {erro}
        </div>
      )}
      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        {canWrite && (
          <Button type='submit' variant='brand' disabled={!formState.isValid || salvando}>
            {salvando ? 'Salvando…' : 'Salvar'}
          </Button>
        )}
        <Button type='button' onClick={onCancel}>
          {canWrite ? 'Cancelar' : 'Fechar'}
        </Button>
      </div>
    </form>
  );
}
