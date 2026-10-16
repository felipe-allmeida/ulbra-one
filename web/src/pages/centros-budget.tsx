import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Plus, Trash2, Wallet } from 'lucide-react';
import { type CostCenter, type CostCenterUnitBudget, type Unit } from '../api';
import { useCostCenterBudgets, useCreateCostCenterBudget, useDeleteCostCenterBudget } from '../queries';
import { Button, ConfirmDialog, CurrencyInput, Field, Input, Modal, Select, SkeletonRows } from '../ui';
import { brl, parseMoneyInput } from '../lib';
import { getErrorMessage } from '../apiError';
import { useToast } from '../toast';
import { fieldError, resolver, unitBudgetSchema, type UnitBudgetValues } from './schemas';
import { currentCompetency, fmtCompetency } from './requisitions/competency';
import { ExpandableRow } from './requisitions/expandable-row';

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
            <div className='cc-activity-table'>
              <table>
                <thead>
                  <tr>
                    <th>Unidade</th>
                    <th>Competência</th>
                    <th style={{ textAlign: 'right' }}>Teto mensal</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  <SkeletonRows rows={3} cols={4} />
                </tbody>
              </table>
            </div>
          ) : budgets.length === 0 ? (
            <div className='activity-empty'>
              <Wallet />
              Nenhum orçamento definido. Adicione o teto mensal de cada unidade por período.
            </div>
          ) : (
            <div className='cc-activity-table'>
              <table>
                <thead>
                  <tr>
                    <th>Unidade</th>
                    <th>Competência</th>
                    <th style={{ textAlign: 'right' }}>Teto mensal</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {budgets.map((b) => (
                    <tr key={b.id}>
                      <td>{acronym(b.unitId)}</td>
                      <td className='t-mono'>{fmtCompetency(b.competency)}</td>
                      <td className='amount-cell' style={{ textAlign: 'right', opacity: b.active ? 1 : 0.5 }}>
                        {brl(b.monthlyLimit)}
                      </td>
                      <td style={{ textAlign: 'right' }}>
                        {canWrite && (
                          <button className='icon-btn' aria-label='Remover' onClick={() => setRemoving(b)}>
                            <Trash2 size={16} />
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
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
