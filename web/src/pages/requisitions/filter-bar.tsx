import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Search, X } from 'lucide-react';
import { type RequisitionStatus, type RequisitionType } from '../../api';
import { useCostCenters, useUnits } from '../../queries';
import { Button, Combobox, CurrencyInput, Input, Select, ToolbarField } from '../../ui';

/** Controlled filter set for the requisitions table. Values are kept as strings (URL-friendly); the caller
 * converts value/competency to the query shape. `statuses` drives the multi-status chip-rail. */
export interface RequisitionFilters {
  search: string;
  unitId: string | null;
  costCenterId: string | null;
  type: RequisitionType | '';
  competencyFrom: string;
  competencyTo: string;
  minValue: string;
  maxValue: string;
  statuses: RequisitionStatus[];
  sort: string;
}

export const EMPTY_FILTERS: RequisitionFilters = {
  search: '',
  unitId: null,
  costCenterId: null,
  type: '',
  competencyFrom: '',
  competencyTo: '',
  minValue: '',
  maxValue: '',
  statuses: [],
  sort: '',
};

export const REQUISITION_TYPES: RequisitionType[] = ['Project', 'ServiceProvision', 'Purchase', 'FixedExpense'];

export function RequisitionFilterBar({
  companyId,
  value,
  onChange,
  statusOptions,
}: {
  companyId: string;
  value: RequisitionFilters;
  onChange: (next: RequisitionFilters) => void;
  /** When provided, renders the multi-status chip-rail (used on the "Encerradas" tab). */
  statusOptions?: RequisitionStatus[];
}) {
  const { t } = useTranslation();
  const units = useUnits(companyId).data ?? [];
  const centros = useCostCenters(companyId).data ?? [];

  // Debounced free-text search: a local text mirror propagated 250ms after the last keystroke. Refs hold the
  // latest value/onChange so the timer never writes a stale filter set when another field changes meanwhile.
  const valueRef = useRef(value);
  valueRef.current = value;
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;
  const [text, setText] = useState(value.search);
  useEffect(() => setText(value.search), [value.search]);
  useEffect(() => {
    if (text === valueRef.current.search) return;
    const id = setTimeout(() => onChangeRef.current({ ...valueRef.current, search: text }), 250);
    return () => clearTimeout(id);
  }, [text]);

  const patch = (p: Partial<RequisitionFilters>) => onChange({ ...value, ...p });
  const toggleStatus = (s: RequisitionStatus) =>
    patch({ statuses: value.statuses.includes(s) ? value.statuses.filter((x) => x !== s) : [...value.statuses, s] });

  const hasFilters =
    !!value.search ||
    !!value.unitId ||
    !!value.costCenterId ||
    !!value.type ||
    !!value.competencyFrom ||
    !!value.competencyTo ||
    !!value.minValue ||
    !!value.maxValue ||
    value.statuses.length > 0 ||
    !!value.sort;

  return (
    <div className='rq-filters'>
      <div className='rq-filters-row'>
        <div className='top-search rq-filters-search'>
          <Search />
          <input
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('requisitions.searchPlaceholder')}
            aria-label={t('requisitions.searchPlaceholder')}
          />
        </div>
        <ToolbarField label={t('requisitions.filters.unit')}>
          <Combobox
            value={value.unitId}
            onChange={(id) => patch({ unitId: id })}
            items={units.map((u) => ({ id: u.id, label: u.acronym, sub: u.name }))}
            placeholder={t('requisitions.filters.allUnits')}
            searchPlaceholder={t('requisitions.filters.unit')}
          />
        </ToolbarField>
        <ToolbarField label={t('requisitions.filters.costCenter')}>
          <Combobox
            value={value.costCenterId}
            onChange={(id) => patch({ costCenterId: id })}
            items={centros.map((c) => ({ id: c.id, label: c.name, sub: c.code }))}
            placeholder={t('requisitions.filters.allCostCenters')}
            searchPlaceholder={t('requisitions.filters.costCenter')}
          />
        </ToolbarField>
        <ToolbarField label={t('requisitions.filters.type')}>
          <Select value={value.type} onChange={(e) => patch({ type: e.target.value as RequisitionType | '' })}>
            <option value=''>{t('requisitions.filters.allTypes')}</option>
            {REQUISITION_TYPES.map((tp) => (
              <option key={tp} value={tp}>
                {t(`requisitions.type.${tp}`)}
              </option>
            ))}
          </Select>
        </ToolbarField>
      </div>

      <div className='rq-filters-row'>
        <ToolbarField label={t('requisitions.filters.competencyFrom')}>
          <Input
            type='month'
            value={value.competencyFrom}
            onChange={(e) => patch({ competencyFrom: e.target.value })}
          />
        </ToolbarField>
        <ToolbarField label={t('requisitions.filters.competencyTo')}>
          <Input type='month' value={value.competencyTo} onChange={(e) => patch({ competencyTo: e.target.value })} />
        </ToolbarField>
        <ToolbarField label={t('requisitions.filters.valueFrom')}>
          <CurrencyInput value={value.minValue} onValueChange={(val) => patch({ minValue: val })} placeholder='0' />
        </ToolbarField>
        <ToolbarField label={t('requisitions.filters.valueTo')}>
          <CurrencyInput value={value.maxValue} onValueChange={(val) => patch({ maxValue: val })} placeholder='∞' />
        </ToolbarField>
        {hasFilters && (
          <Button variant='ghost' icon={<X />} onClick={() => onChange(EMPTY_FILTERS)}>
            {t('requisitions.filters.clear')}
          </Button>
        )}
      </div>

      {statusOptions && statusOptions.length > 0 && (
        <div className='rqf-chips' role='group' aria-label={t('requisitions.filters.status')}>
          {statusOptions.map((s) => (
            <button
              key={s}
              type='button'
              className={`rqf-chip ${value.statuses.includes(s) ? 'on' : ''}`}
              aria-pressed={value.statuses.includes(s)}
              onClick={() => toggleStatus(s)}
            >
              {t(`requisitions.status.${s}`)}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
