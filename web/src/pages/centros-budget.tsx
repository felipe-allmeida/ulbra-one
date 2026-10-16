import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Plus, Trash2, Wallet } from 'lucide-react';
import { type CostCenter, type CostCenterUnitBudget, type Unit } from '../api';
import { useCostCenterBudgets, useCreateCostCenterBudget, useDeleteCostCenterBudget } from '../queries';
import { Button, ConfirmDialog, CurrencyInput, Field, Input, LoadingState, Modal, Select } from '../ui';
import { brl, parseMoneyInput } from '../lib';
import { getErrorMessage } from '../apiError';
import { useToast } from '../toast';
import { fieldError, resolver, unitBudgetSchema, type UnitBudgetValues } from './schemas';
import { currentCompetency, fmtCompetency } from './requisitions/competency';
import { ExpandableRow } from './requisitions/expandable-row';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';

/**
 * Orçamento do centro de custo (AEL-830): teto mensal por unidade × competência, dono no Core. O
 * "orçamento anual" do CC é o roll-up destas células. Criação por período (materializa 1 célula/mês).
 */
export function CostCenterBudgetPanel({
  centro,
  unidades,
  canWrite,
}: {
  centro: CostCenter;
  unidades: Unit[];
  canWrite: boolean;
}) {
  const toast = useToast();
  const budgetsQuery = useCostCenterBudgets(centro.id);
  const budgets = budgetsQuery.data ?? [];
  const del = useDeleteCostCenterBudget(centro.id);

  const [addOpen, setAddOpen] = useState(false);
  const [removing, setRemoving] = useState<CostCenterUnitBudget | null>(null);

  const acronym = (unitId: string) => unidades.find((u) => u.id === unitId)?.acronym ?? '—';
  const total = budgets.filter((b) => b.active).reduce((s, b) => s + b.monthlyLimit, 0);
  const semUnidade = centro.unitIds.length === 0;

  return (
    <>
      <div className='cc-budget-accordion'>
        <ExpandableRow
          title='Orçamento por unidade'
          meta={!semUnidade && budgets.length > 0 ? `${budgets.length} célula(s)` : undefined}
          value={!semUnidade && budgets.length > 0 ? `soma mensal ${brl(total)}` : undefined}
          defaultOpen
        >
          {canWrite && !semUnidade && (
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 10 }}>
              <Button variant='brand' icon={<Plus />} onClick={() => setAddOpen(true)}>
                Adicionar orçamento
              </Button>
            </div>
          )}

          {semUnidade ? (
            <div className='activity-empty'>
              <Wallet />
              Associe unidades a esta natureza orçamentária (em Editar) para definir orçamentos.
            </div>
          ) : budgetsQuery.isLoading ? (
            <LoadingState />
          ) : budgets.length === 0 ? (
            <div className='activity-empty'>
              <Wallet />
              Nenhum orçamento definido. Adicione o teto mensal de cada unidade por período.
            </div>
          ) : (
            <CostCenterBudgetTable budgets={budgets} acronym={acronym} canWrite={canWrite} onRemove={setRemoving} />
          )}
        </ExpandableRow>
      </div>

      {addOpen && <AddBudgetModal centro={centro} unidades={unidades} onClose={() => setAddOpen(false)} />}

      {removing && (
        <ConfirmDialog
          title='Remover orçamento'
          danger
          confirmLabel='Remover'
          busy={del.isPending}
          message={`Remover o orçamento de ${acronym(removing.unitId)} em ${fmtCompetency(removing.competency)}?`}
          onConfirm={() =>
            del.mutate(removing.id, {
              onError: (e) => toast.error(getErrorMessage(e)),
              onSuccess: () => {
                setRemoving(null);
                toast.success('Orçamento removido.');
              },
            })
          }
          onCancel={() => setRemoving(null)}
        />
      )}
    </>
  );
}

/**
 * Ordem = unidade/competência/teto mensal/ações, igual a hoje. `unidade` (sigla, sempre presente) é
 * o `title`; `competência` é o único `meta`; `teto mensal` é o único valor monetário e vira
 * `amount` (mantém a opacidade reduzida quando a célula está inativa); `remover` ganha
 * `align: 'right'` — o `<td>` original já usava `textAlign: 'right'` inline (sem `.td-action`,
 * mas o mesmo alinhamento) e `header: ''` porque o `<th>` original era vazio. Sem `onRowClick` — a
 * linha nunca navegou.
 */
function CostCenterBudgetTable({
  budgets,
  acronym,
  canWrite,
  onRemove,
}: {
  budgets: CostCenterUnitBudget[];
  acronym: (unitId: string) => string;
  canWrite: boolean;
  onRemove: (b: CostCenterUnitBudget) => void;
}) {
  const columns: Column<CostCenterUnitBudget>[] = [
    { key: 'unit', header: 'Unidade', role: 'title', cell: (b) => acronym(b.unitId) },
    {
      key: 'competency',
      header: 'Competência',
      role: 'meta',
      cell: (b) => <span className='t-mono'>{fmtCompetency(b.competency)}</span>,
    },
    {
      key: 'monthlyLimit',
      header: 'Teto mensal',
      role: 'amount',
      align: 'right',
      cell: (b) => (
        <span className='amount-cell' style={{ opacity: b.active ? 1 : 0.5 }}>
          {brl(b.monthlyLimit)}
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (b) =>
        canWrite && (
          <button className='btn btn-icon' aria-label='Remover' title='Remover' onClick={() => onRemove(b)}>
            <Trash2 size={16} />
          </button>
        ),
    },
  ];

  return <ResponsiveTable rows={budgets} columns={columns} rowKey={(b) => b.id} />;
}

/** Modal de criação por período: teto mensal de uma unidade em [início, fim] (materializa 1 célula/mês). */
function AddBudgetModal({ centro, unidades, onClose }: { centro: CostCenter; unidades: Unit[]; onClose: () => void }) {
  const toast = useToast();
  const create = useCreateCostCenterBudget(centro.id);
  const unitOptions = unidades.filter((u) => centro.unitIds.includes(u.id));

  const { control, handleSubmit, formState } = useForm<UnitBudgetValues>({
    resolver: resolver(unitBudgetSchema),
    mode: 'onChange',
    defaultValues: {
      unitId: '',
      startCompetency: currentCompetency(),
      endCompetency: '',
      monthlyLimit: '',
      active: true,
    },
  });

  const onValid = (v: UnitBudgetValues) =>
    create.mutate(
      {
        unitId: v.unitId,
        startCompetency: v.startCompetency,
        endCompetency: v.endCompetency || undefined,
        monthlyLimit: parseMoneyInput(v.monthlyLimit) ?? 0,
      },
      {
        onError: (e) => toast.error(getErrorMessage(e)),
        onSuccess: (r) => {
          toast.success(`${r.count} competência(s) orçada(s).`);
          onClose();
        },
      },
    );

  return (
    <Modal
      title='Adicionar orçamento'
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose} disabled={create.isPending}>
            Cancelar
          </Button>
          <Button variant='brand' disabled={create.isPending} onClick={() => void handleSubmit(onValid)()}>
            {create.isPending ? 'Salvando…' : 'Salvar'}
          </Button>
        </>
      }
    >
      <div className='edit-grid'>
        <Field label='Unidade'>
          <Controller
            control={control}
            name='unitId'
            render={({ field }) => (
              <Select {...field}>
                <option value=''>Selecione</option>
                {unitOptions.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.acronym} — {u.name}
                  </option>
                ))}
              </Select>
            )}
          />
          {fieldError(formState.errors.unitId?.message) && (
            <div className='field-err'>{fieldError(formState.errors.unitId?.message)}</div>
          )}
        </Field>
        <Field label='Teto mensal (R$)'>
          <Controller
            control={control}
            name='monthlyLimit'
            render={({ field }) => (
              <CurrencyInput
                value={field.value}
                onValueChange={field.onChange}
                onBlur={field.onBlur}
                name={field.name}
                placeholder='ex.: 50.000,00'
              />
            )}
          />
          {fieldError(formState.errors.monthlyLimit?.message) && (
            <div className='field-err'>{fieldError(formState.errors.monthlyLimit?.message)}</div>
          )}
        </Field>
        <Field label='Competência inicial'>
          <Controller
            control={control}
            name='startCompetency'
            render={({ field }) => <Input {...field} type='month' />}
          />
          {fieldError(formState.errors.startCompetency?.message) && (
            <div className='field-err'>{fieldError(formState.errors.startCompetency?.message)}</div>
          )}
        </Field>
        <Field label='Competência final' help='Vazio = 12 meses a partir do início.'>
          <Controller
            control={control}
            name='endCompetency'
            render={({ field }) => <Input {...field} type='month' />}
          />
          {fieldError(formState.errors.endCompetency?.message) && (
            <div className='field-err'>{fieldError(formState.errors.endCompetency?.message)}</div>
          )}
        </Field>
      </div>
    </Modal>
  );
}
