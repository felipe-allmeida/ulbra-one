import { useEffect } from 'react';
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Controller, useForm, useWatch, type Control } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ChevronLeft } from 'lucide-react';
import { useApp } from '../../store';
import { useBudgetAdjustment, useCostCenters, useSaveBudgetAdjustment, useUnits } from '../../queries';
import { Button, CurrencyInput, Field, Input, LoadingState, PageHeader, Panel, Select } from '../../ui';
import { getErrorMessage } from '../../apiError';
import { parseMoneyInput } from '../../lib';
import { budgetAdjustmentSchema, ADEQUACAO_TYPES, fieldError, resolver, type BudgetAdjustmentValues } from '../schemas';
import { type BudgetAdjustment } from '../../api';
import { AdjustmentBudgetContext } from './adjustment-context';

export function NewBudgetAdjustmentPage() {
  return <BudgetAdjustmentFormPage />;
}

export function EditBudgetAdjustmentPage() {
  const { id } = useParams();
  const existingQuery = useBudgetAdjustment(id);
  const existing = existingQuery.data;
  if (existingQuery.isLoading) return <LoadingState />;
  if (!existing) return null;
  return <BudgetAdjustmentFormPage initial={existing} />;
}

function BudgetAdjustmentFormPage({ initial }: { initial?: BudgetAdjustment }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const location = useLocation();
  const { empresa, empresaId } = useApp();
  const editing = !!initial;

  return (
    <>
      <PageHeader title={t(editing ? 'budgetAdjustments.form.editTitle' : 'budgetAdjustments.form.newTitle')}>
        <Button
          icon={<ChevronLeft />}
          onClick={() =>
            nav(editing ? `/app/budget-adjustments/${initial.id}` : '/app/budget-adjustments', {
              state: location.state,
            })
          }
        >
          {t('budgetAdjustments.back')}
        </Button>
      </PageHeader>
      {empresa && <BudgetAdjustmentForm companyId={empresaId} initial={initial} />}
    </>
  );
}

function BudgetAdjustmentForm({ companyId, initial }: { companyId: string; initial?: BudgetAdjustment }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const location = useLocation();
  const editing = !!initial;
  const unidades = useUnits(companyId).data ?? [];
  const centros = useCostCenters(companyId).data ?? [];

  // Prefill coming from triage ("Solicitar adequação"): cost-center/unit/competency/overage value.
  // Creation only (dimensions are immutable on edit); CC and unit come together from the requisition, so
  // the unit always belongs to the CC.
  const [searchParams] = useSearchParams();
  const prefill = editing ? null : searchParams;

  const { control, handleSubmit, setValue, getValues, formState } = useForm<BudgetAdjustmentValues>({
    resolver: resolver(budgetAdjustmentSchema),
    mode: 'onChange',
    defaultValues: {
      unitId: initial?.unitId ?? prefill?.get('unitId') ?? '',
      costCenterId: initial?.costCenterId ?? prefill?.get('costCenterId') ?? '',
      type: initial?.type ?? 'Supplementation',
      competency: initial?.competency ?? prefill?.get('competency') ?? '',
      sourceCompetency: initial?.sourceCompetency ?? '',
      value: initial ? String(initial.value) : (prefill?.get('value') ?? ''),
      justification: initial?.justification ?? '',
      counterparts: initial?.counterparts ?? '',
    },
  });

  const type = useWatch({ control, name: 'type' });
  const isTransfer = type === 'Transfer';
  const isReduction = type === 'Reduction';

  // A unidade da adequação deve pertencer ao centro de custo selecionado (novo modelo cost_center → units).
  const watchedCostCenterId = useWatch({ control, name: 'costCenterId' });
  const selectedCentro = centros.find((c) => c.id === watchedCostCenterId);
  const unitOptions = selectedCentro ? unidades.filter((u) => selectedCentro.unitIds.includes(u.id)) : unidades;
  // Espelho: escolhendo a unidade primeiro, só as naturezas registradas nela (unitIds) ficam disponíveis.
  const watchedUnitId = useWatch({ control, name: 'unitId' });
  const costCenterOptions = watchedUnitId ? centros.filter((c) => c.unitIds.includes(watchedUnitId)) : centros;

  // When leaving Transfer, clear the source competency (otherwise the backend rejects with SourceNotAllowed).
  useEffect(() => {
    if (!isTransfer) setValue('sourceCompetency', '', { shouldValidate: true });
  }, [isTransfer, setValue]);

  const mutation = useSaveBudgetAdjustment({
    onSuccess: (a) => nav(`/app/budget-adjustments/${a.id}`, { state: location.state }),
  });
  const erro = mutation.error ? getErrorMessage(mutation.error) : null;

  const onValid = (v: BudgetAdjustmentValues) =>
    mutation.mutate({
      id: initial?.id,
      unitId: v.unitId,
      costCenterId: v.costCenterId,
      type: v.type,
      competency: v.competency,
      sourceCompetency: v.type === 'Transfer' ? v.sourceCompetency || null : null,
      value: parseMoneyInput(v.value) ?? 0,
      justification: v.justification.trim(),
      counterparts: v.counterparts?.trim() || null,
    });

  return (
    <div className='adj-form-grid'>
      <Panel title={t('budgetAdjustments.form.section')} padded>
        <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
          <div className='edit-grid'>
            <Field label={t('budgetAdjustments.fields.type')} help={t('budgetAdjustments.fields.typeHelp')}>
              <Controller
                control={control}
                name='type'
                render={({ field }) => (
                  <Select {...field}>
                    {ADEQUACAO_TYPES.map((ty) => (
                      <option key={ty} value={ty}>
                        {t(`budgetAdjustments.type.${ty}`)}
                      </option>
                    ))}
                  </Select>
                )}
              />
            </Field>

            <Field label={t('budgetAdjustments.fields.value')} help={t('budgetAdjustments.fields.valueHelp')}>
              <Controller
                control={control}
                name='value'
                render={({ field }) => (
                  <CurrencyInput
                    value={field.value}
                    onValueChange={field.onChange}
                    onBlur={field.onBlur}
                    name={field.name}
                    placeholder='ex.: 5.000,00'
                  />
                )}
              />
              {fieldError(formState.errors.value?.message) && (
                <div className='field-err'>{fieldError(formState.errors.value?.message)}</div>
              )}
            </Field>

            <Field label={t('budgetAdjustments.fields.costCenter')}>
              <Controller
                control={control}
                name='costCenterId'
                render={({ field }) => (
                  <Select
                    {...field}
                    disabled={editing}
                    onChange={(e) => {
                      field.onChange(e);
                      // Só limpa a unidade se ela não pertencer ao novo CC — permite escolher unidade antes do CC.
                      const cc = centros.find((c) => c.id === e.target.value);
                      const u = getValues('unitId');
                      if (u && cc && !cc.unitIds.includes(u)) setValue('unitId', '');
                    }}
                  >
                    <option value=''>{t('common.select')}</option>
                    {costCenterOptions.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.code} · {c.name}
                      </option>
                    ))}
                  </Select>
                )}
              />
              {fieldError(formState.errors.costCenterId?.message) && (
                <div className='field-err'>{fieldError(formState.errors.costCenterId?.message)}</div>
              )}
            </Field>

            <Field label={t('budgetAdjustments.fields.unit')}>
              <Controller
                control={control}
                name='unitId'
                render={({ field }) => (
                  <Select
                    {...field}
                    disabled={editing}
                    onChange={(e) => {
                      field.onChange(e);
                      // Recíproco: limpa o CC se ele não pertencer à nova unidade.
                      const cc = centros.find((c) => c.id === getValues('costCenterId'));
                      if (cc && e.target.value && !cc.unitIds.includes(e.target.value)) setValue('costCenterId', '');
                    }}
                  >
                    <option value=''>{t('common.select')}</option>
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

            {isTransfer && (
              <Field
                label={t('budgetAdjustments.fields.sourceCompetency')}
                help={t('budgetAdjustments.fields.sourceCompetencyHelp')}
              >
                <Controller
                  control={control}
                  name='sourceCompetency'
                  render={({ field }) => <Input {...field} type='month' />}
                />
                {fieldError(formState.errors.sourceCompetency?.message) && (
                  <div className='field-err'>{fieldError(formState.errors.sourceCompetency?.message)}</div>
                )}
              </Field>
            )}

            <Field
              label={t(
                isTransfer ? 'budgetAdjustments.fields.destinationCompetency' : 'budgetAdjustments.fields.competency',
              )}
            >
              <Controller
                control={control}
                name='competency'
                render={({ field }) => <Input {...field} type='month' />}
              />
              {fieldError(formState.errors.competency?.message) && (
                <div className='field-err'>{fieldError(formState.errors.competency?.message)}</div>
              )}
            </Field>

            <Field label={t('budgetAdjustments.fields.justification')}>
              <Controller
                control={control}
                name='justification'
                render={({ field }) => (
                  <textarea
                    className='input'
                    rows={3}
                    value={field.value}
                    onChange={field.onChange}
                    onBlur={field.onBlur}
                    placeholder={t('budgetAdjustments.fields.justificationPlaceholder')}
                  />
                )}
              />
              {fieldError(formState.errors.justification?.message) && (
                <div className='field-err'>{fieldError(formState.errors.justification?.message)}</div>
              )}
            </Field>

            <Field
              label={t('budgetAdjustments.fields.counterparts')}
              help={t(
                isReduction
                  ? 'budgetAdjustments.fields.counterpartsOptional'
                  : 'budgetAdjustments.fields.counterpartsHelp',
              )}
            >
              <Controller
                control={control}
                name='counterparts'
                render={({ field }) => (
                  <textarea
                    className='input'
                    rows={2}
                    value={field.value ?? ''}
                    onChange={field.onChange}
                    onBlur={field.onBlur}
                    placeholder={
                      isReduction ? t('common.optional') : t('budgetAdjustments.fields.counterpartsPlaceholder')
                    }
                  />
                )}
              />
              {fieldError(formState.errors.counterparts?.message) && (
                <div className='field-err'>{fieldError(formState.errors.counterparts?.message)}</div>
              )}
            </Field>
          </div>

          {erro && <div className='field-err'>{erro}</div>}

          <div className='form-actions'>
            <Button type='button' onClick={() => nav('/app/budget-adjustments')}>
              {t('common.cancel')}
            </Button>
            <Button type='submit' variant='brand' disabled={mutation.isPending}>
              {mutation.isPending ? t('common.saving') : t('common.save')}
            </Button>
          </div>
        </form>
      </Panel>

      <Panel title={t('budgetAdjustments.context.title')}>
        <BudgetAdjustmentContext control={control} />
      </Panel>
    </div>
  );
}

/** Real budget context of the affected competencies: current ceiling → adjustment → resulting ceiling. */
function BudgetAdjustmentContext({ control }: { control: Control<BudgetAdjustmentValues> }) {
  const { t } = useTranslation();
  const type = useWatch({ control, name: 'type' });
  const unitId = useWatch({ control, name: 'unitId' });
  const costCenterId = useWatch({ control, name: 'costCenterId' });
  const competency = useWatch({ control, name: 'competency' });
  const sourceCompetency = useWatch({ control, name: 'sourceCompetency' });
  const value = parseMoneyInput(useWatch({ control, name: 'value' }) ?? '') ?? 0;
  // Reduction subtracts from the ceiling; Supplementation (and a Transfer's destination) adds.
  const mainDirection = type === 'Reduction' ? 'remove' : 'add';

  return (
    <div style={{ display: 'grid', gap: 16 }}>
      {type === 'Transfer' && (
        <div>
          <div style={{ fontSize: 12, fontWeight: 600, opacity: 0.7, padding: '0 16px' }}>
            {t('budgetAdjustments.context.source')}
          </div>
          <AdjustmentBudgetContext
            unitId={unitId}
            costCenterId={costCenterId}
            competency={sourceCompetency}
            value={value}
            direction='remove'
          />
        </div>
      )}
      <div>
        {type === 'Transfer' && (
          <div style={{ fontSize: 12, fontWeight: 600, opacity: 0.7, padding: '0 16px' }}>
            {t('budgetAdjustments.context.destination')}
          </div>
        )}
        <AdjustmentBudgetContext
          unitId={unitId}
          costCenterId={costCenterId}
          competency={competency}
          value={value}
          direction={mainDirection}
        />
      </div>
    </div>
  );
}
