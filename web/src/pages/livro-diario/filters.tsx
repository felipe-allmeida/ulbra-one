import { useTranslation } from 'react-i18next';
import { Input, ToolbarField } from '../../ui';
import { ConsolidatedPicker } from '../contabil-shared';

/** Company (consolidated) + competence range filters for the Livro Diário. */
export function BookFilters({
  companyId,
  from,
  to,
  onCompanyChange,
  onFromChange,
  onToChange,
}: {
  companyId: string;
  from: string;
  to: string;
  onCompanyChange: (v: string) => void;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <ConsolidatedPicker value={companyId} onChange={onCompanyChange} />
      <ToolbarField label={t('accounting.journal.filters.from')}>
        <Input type='date' value={from} onChange={(e) => onFromChange(e.target.value)} />
      </ToolbarField>
      <ToolbarField label={t('accounting.journal.filters.to')}>
        <Input type='date' value={to} onChange={(e) => onToChange(e.target.value)} />
      </ToolbarField>
    </>
  );
}
