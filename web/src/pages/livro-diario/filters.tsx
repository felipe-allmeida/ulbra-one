import { useTranslation } from 'react-i18next';
import { Input, Select, ToolbarField } from '../../ui';
import { useApp } from '../../store';
import { useUnits } from '../../queries';

/** Competence range + unit filters for the Livro Diário. The empresa/consolidado scope comes from the
 *  topbar selector (`useApp`), not from a second control on the page. Unit is company-scoped: within the
 *  selected company the user can drill into one unit (the filter keeps whole entries). */
export function BookFilters({
  from,
  to,
  unitId,
  onFromChange,
  onToChange,
  onUnitChange,
}: {
  from: string;
  to: string;
  unitId: string;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
  onUnitChange: (v: string) => void;
}) {
  const { t } = useTranslation();
  const { scopeCompanyId } = useApp();
  const companyId = scopeCompanyId ?? '';
  const units = useUnits(companyId);
  return (
    <>
      <ToolbarField label={t('accounting.journal.filters.from')}>
        <Input type='date' value={from} onChange={(e) => onFromChange(e.target.value)} />
      </ToolbarField>
      <ToolbarField label={t('accounting.journal.filters.to')}>
        <Input type='date' value={to} onChange={(e) => onToChange(e.target.value)} />
      </ToolbarField>
      <ToolbarField label={t('accounting.generalLedger.filters.unit')}>
        <Select value={unitId} onChange={(e) => onUnitChange(e.target.value)} disabled={!companyId}>
          <option value=''>{t('accounting.generalLedger.filters.allUnits')}</option>
          {(units.data ?? []).map((u) => (
            <option key={u.id} value={u.id}>
              {u.acronym} — {u.name}
            </option>
          ))}
        </Select>
      </ToolbarField>
    </>
  );
}
