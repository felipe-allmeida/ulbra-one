import { useTranslation } from 'react-i18next';
import { Input, Select, ToolbarField } from '../../ui';
import { ConsolidatedPicker } from '../contabil-shared';
import { useCostCenters } from '../../queries';

/** Empresa (consolidado) + raiz da conta (1–5) + intervalo de competência + centro de custo. */
export function LedgerFilters({
  companyId,
  root,
  from,
  to,
  costCenterId,
  onCompanyChange,
  onRootChange,
  onFromChange,
  onToChange,
  onCostCenterChange,
}: {
  companyId: string;
  root: string;
  from: string;
  to: string;
  costCenterId: string;
  onCompanyChange: (v: string) => void;
  onRootChange: (v: string) => void;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
  onCostCenterChange: (v: string) => void;
}) {
  const { t } = useTranslation();
  const costCenters = useCostCenters(companyId);
  return (
    <>
      <ConsolidatedPicker value={companyId} onChange={onCompanyChange} />
      <ToolbarField label={t('accounting.generalLedger.filters.accounts')}>
        <Select value={root} onChange={(e) => onRootChange(e.target.value)}>
          <option value=''>{t('accounting.generalLedger.roots.all')}</option>
          <option value='1'>{t('accounting.generalLedger.roots.1')}</option>
          <option value='2'>{t('accounting.generalLedger.roots.2')}</option>
          <option value='3'>{t('accounting.generalLedger.roots.3')}</option>
          <option value='4'>{t('accounting.generalLedger.roots.4')}</option>
          <option value='5'>{t('accounting.generalLedger.roots.5')}</option>
        </Select>
      </ToolbarField>
      <ToolbarField label={t('accounting.journal.filters.from')}>
        <Input type='date' value={from} onChange={(e) => onFromChange(e.target.value)} />
      </ToolbarField>
      <ToolbarField label={t('accounting.journal.filters.to')}>
        <Input type='date' value={to} onChange={(e) => onToChange(e.target.value)} />
      </ToolbarField>
      <ToolbarField label={t('accounting.generalLedger.filters.costCenter')}>
        <Select value={costCenterId} onChange={(e) => onCostCenterChange(e.target.value)} disabled={!companyId}>
          <option value=''>{t('accounting.generalLedger.filters.allCostCenters')}</option>
          {(costCenters.data ?? []).map((cc) => (
            <option key={cc.id} value={cc.id}>
              {cc.code} — {cc.name}
            </option>
          ))}
        </Select>
      </ToolbarField>
    </>
  );
}
