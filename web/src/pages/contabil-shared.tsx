import { useTranslation } from 'react-i18next';
import { useCompanies } from '../queries';
import { maskCnpj } from '../format';
import { Select, ToolbarField } from '../ui';

/**
 * Company / consolidated picker for the accounting reports. An empty value means
 * "consolidated" (all companies); a selected id scopes the report to one company.
 * When a single company is selected, its CNPJ is shown as an informative caption
 * anchored below the dropdown (out of flow, so it doesn't shift the filter row).
 */
export function ConsolidatedPicker({ value, onChange }: { value: string; onChange: (id: string) => void }) {
  const { t } = useTranslation();
  const companies = useCompanies().data ?? [];
  const selected = companies.find((c) => c.id === value);
  return (
    <ToolbarField label={t('accounting.company')}>
      <div className='picker-cnpj-anchor'>
        <Select value={value} onChange={(e) => onChange(e.target.value)}>
          <option value=''>{t('accounting.consolidated')}</option>
          {companies.map((c) => (
            <option key={c.id} value={c.id}>
              {c.legalName}
            </option>
          ))}
        </Select>
        {selected && (
          <span className='picker-cnpj'>
            {t('accounting.cnpj')} {maskCnpj(selected.cnpj)}
          </span>
        )}
      </div>
    </ToolbarField>
  );
}
