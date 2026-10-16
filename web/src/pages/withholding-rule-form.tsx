import { useForm, Controller } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Field, Input, Select } from '../ui';
import { useToast } from '../toast';
import { getErrorMessage } from '../apiError';
import { useCompanies, useSaveWithholdingRule } from '../queries';
import {
  withholdingRuleSchema,
  resolver,
  fieldError,
  WITHHOLDING_TAX_TYPES,
  RATE_SOURCES,
  BASE_RULES,
  NON_RETENTION_REASONS,
  TAX_REGIMES,
  SIMPLES_ANEXOS,
  type WithholdingRuleValues,
} from './schemas';
import { FormActions } from './registry-forms';
import { fractionToPercent, percentToFraction } from '../lib';
import type { WithholdingRule, WithholdingRuleInput } from '../api';

/**
 * Create/edit form for a WithholdingRule (Regra de Retenção). Mirrors VendorForm (RHF + Controller + resolver).
 * The rule is a set of matching dimensions (regime / CPRB / anexo / subitem / município / empresa; null = "qualquer")
 * + the outcome (retém, alíquota, base, mínimo). The rate is entered as a percentage and stored as a fraction.
 */
export function WithholdingRuleForm({
  initial,
  onSaved,
  onCancel,
}: {
  initial?: WithholdingRule;
  onSaved: (rule: WithholdingRule) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const companies = useCompanies();

  const { control, handleSubmit, watch, formState } = useForm<WithholdingRuleValues>({
    resolver: resolver(withholdingRuleSchema),
    mode: 'onChange',
    defaultValues: {
      taxType: initial?.taxType ?? 'Irrf',
      retains: initial?.retains ?? true,
      reason: initial?.reason ?? 'None',
      rateSource: initial?.rateSource ?? 'Fixed',
      rate: initial?.rate != null ? String(fractionToPercent(initial.rate)) : '',
      baseRule: initial?.baseRule ?? 'Full',
      minimumAmount: initial?.minimumAmount != null ? String(initial.minimumAmount) : '',
      lc116Subitem: initial?.lc116Subitem ?? '',
      vendorRegime: initial?.vendorRegime ?? null,
      simplesAnexo: initial?.simplesAnexo ?? null,
      cprb: initial?.cprb ?? null,
      municipalityIbge: initial?.municipalityIbge ?? '',
      companyId: initial?.companyId ?? '',
    },
  });

  const mutation = useSaveWithholdingRule({
    onSuccess: (saved) => {
      toast.success(initial ? t('retentionRules.updated') : t('retentionRules.created'));
      onSaved(saved);
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });
  const saving = mutation.isPending;

  const retains = watch('retains');
  const rateSource = watch('rateSource');
  const vendorRegime = watch('vendorRegime');
  const fixedRate = retains && rateSource === 'Fixed';

  const onValid = (v: WithholdingRuleValues) => {
    const body: WithholdingRuleInput = {
      companyId: v.companyId || null,
      taxType: v.taxType,
      lc116Subitem: v.lc116Subitem.trim() || null,
      vendorRegime: v.vendorRegime,
      municipalityIbge: v.municipalityIbge.trim() || null,
      retains: v.retains,
      reason: v.retains ? 'None' : v.reason, // motivo só se aplica quando NÃO retém
      rateSource: v.rateSource,
      // Alíquota fixa (fração) só quando retém e a origem é fixa; senão a alíquota vem da nota ou não há.
      rate: v.rateSource === 'Fixed' && v.retains ? percentToFraction(Number(v.rate)) : null,
      baseRule: v.baseRule,
      minimumAmount: v.minimumAmount.trim() ? Number(v.minimumAmount) : null,
      cprb: v.cprb,
      // Anexo só faz sentido para Simples — zera fora dele.
      simplesAnexo: v.vendorRegime === 'SimplesNacional' ? v.simplesAnexo : null,
    };
    mutation.mutate({ id: initial?.id, body });
  };

  return (
    <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <div className='form-grid'>
        <Field label={t('retentionRules.fields.taxType')}>
          <Controller
            control={control}
            name='taxType'
            render={({ field }) => (
              <Select {...field}>
                {WITHHOLDING_TAX_TYPES.map((v) => (
                  <option key={v} value={v}>
                    {t(`withholdingTaxType.${v}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>

        <Field label={t('retentionRules.fields.retains')}>
          <Controller
            control={control}
            name='retains'
            render={({ field }) => (
              <Select value={String(field.value)} onChange={(e) => field.onChange(e.target.value === 'true')}>
                <option value='true'>{t('retentionRules.yes')}</option>
                <option value='false'>{t('retentionRules.no')}</option>
              </Select>
            )}
          />
        </Field>

        {!retains && (
          <Field label={t('retentionRules.fields.reason')}>
            <Controller
              control={control}
              name='reason'
              render={({ field }) => (
                <Select {...field}>
                  {NON_RETENTION_REASONS.map((v) => (
                    <option key={v} value={v}>
                      {t(`nonRetentionReason.${v}`)}
                    </option>
                  ))}
                </Select>
              )}
            />
          </Field>
        )}

        <Field label={t('retentionRules.fields.rateSource')}>
          <Controller
            control={control}
            name='rateSource'
            render={({ field }) => (
              <Select {...field}>
                {RATE_SOURCES.map((v) => (
                  <option key={v} value={v}>
                    {t(`rateSource.${v}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>

        {fixedRate && (
          <Field label={t('retentionRules.fields.rate')} help={t('retentionRules.ratePercentHelp')}>
            <Controller
              control={control}
              name='rate'
              render={({ field }) => <Input {...field} type='number' step='0.0001' min='0' placeholder='ex.: 11' />}
            />
            {fieldError(formState.errors.rate?.message) && (
              <div className='field-err'>{fieldError(formState.errors.rate?.message)}</div>
            )}
          </Field>
        )}

        <Field label={t('retentionRules.fields.baseRule')}>
          <Controller
            control={control}
            name='baseRule'
            render={({ field }) => (
              <Select {...field}>
                {BASE_RULES.map((v) => (
                  <option key={v} value={v}>
                    {t(`baseRule.${v}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>

        <Field label={t('retentionRules.fields.minimumAmount')}>
          <Controller
            control={control}
            name='minimumAmount'
            render={({ field }) => <Input {...field} type='number' step='0.01' min='0' placeholder='—' />}
          />
        </Field>

        <Field label={t('retentionRules.fields.lc116Subitem')}>
          <Controller
            control={control}
            name='lc116Subitem'
            render={({ field }) => <Input {...field} placeholder='ex.: 11.02' />}
          />
        </Field>

        <Field label={t('retentionRules.fields.vendorRegime')}>
          <Controller
            control={control}
            name='vendorRegime'
            render={({ field }) => (
              <Select
                value={field.value ?? ''}
                onChange={(e) =>
                  field.onChange(e.target.value === '' ? null : (e.target.value as (typeof TAX_REGIMES)[number]))
                }
              >
                <option value=''>{t('retentionRules.anyRegime')}</option>
                {TAX_REGIMES.map((v) => (
                  <option key={v} value={v}>
                    {t(`taxRegime.${v}`)}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>

        {vendorRegime === 'SimplesNacional' && (
          <Field label={t('retentionRules.fields.simplesAnexo')}>
            <Controller
              control={control}
              name='simplesAnexo'
              render={({ field }) => (
                <Select
                  value={field.value ?? ''}
                  onChange={(e) =>
                    field.onChange(e.target.value === '' ? null : (e.target.value as (typeof SIMPLES_ANEXOS)[number]))
                  }
                >
                  <option value=''>{t('registry.vendor.simplesAnexoNone')}</option>
                  {SIMPLES_ANEXOS.map((a) => (
                    <option key={a} value={a}>
                      Anexo {a}
                    </option>
                  ))}
                </Select>
              )}
            />
          </Field>
        )}

        <Field label={t('retentionRules.fields.cprb')}>
          <Controller
            control={control}
            name='cprb'
            render={({ field }) => (
              <Select
                value={field.value == null ? '' : String(field.value)}
                onChange={(e) => field.onChange(e.target.value === '' ? null : e.target.value === 'true')}
              >
                <option value=''>{t('registry.vendor.cprbUnknown')}</option>
                <option value='true'>{t('registry.vendor.cprbYes')}</option>
                <option value='false'>{t('registry.vendor.cprbNo')}</option>
              </Select>
            )}
          />
        </Field>

        <Field label={t('retentionRules.fields.municipalityIbge')}>
          <Controller
            control={control}
            name='municipalityIbge'
            render={({ field }) => <Input {...field} placeholder='ex.: 4304606' />}
          />
        </Field>

        <Field label={t('retentionRules.fields.company')}>
          <Controller
            control={control}
            name='companyId'
            render={({ field }) => (
              <Select {...field}>
                <option value=''>{t('retentionRules.allCompanies')}</option>
                {(companies.data ?? []).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.legalName}
                  </option>
                ))}
              </Select>
            )}
          />
        </Field>
      </div>

      <FormActions valid={formState.isValid} saving={saving} onCancel={onCancel} editing={!!initial} />
    </form>
  );
}
