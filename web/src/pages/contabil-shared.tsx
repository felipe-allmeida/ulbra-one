import { useTranslation } from 'react-i18next';
import { useCompanies } from '../queries';
import { Select, ToolbarField } from '../ui';

/**
 * Company / consolidated picker for the accounting reports. An empty value means
 * "consolidated" (all companies); a selected id scopes the report to one company.
 */
export function ConsolidatedPicker({ value, onChange }: { value: string; onChange: (id: string) => void }) {
  const { t } = useTranslation();
  const companies = useCompanies().data ?? [];
  return (
    <ToolbarField label={t('accounting.company')}>
      <Select value={value} onChange={(e) => onChange(e.target.value)}>
        <option value=''>{t('accounting.consolidated')}</option>
        {companies.map((c) => (
          <option key={c.id} value={c.id}>
            {c.legalName}
          </option>
        ))}
      </Select>
    </ToolbarField>
  );
}
