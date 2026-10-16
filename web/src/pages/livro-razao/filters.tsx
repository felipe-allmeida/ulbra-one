import { useTranslation } from 'react-i18next';
import { Download } from 'lucide-react';
import { Button, Combobox, Input, Select, ToolbarField } from '../../ui';
import { useApp } from '../../store';
import { useCostCenters, useLedgerAccounts, useUnits } from '../../queries';

/** Filter toolbar for the Livro Razão, laid out in two fixed rows so selecting a long account label
 *  never resizes a field or reorders the row: row 1 = account group + specific account (with the export
 *  button pinned to the far right), row 2 = competence range + unidade + cost center. The group
 *  dropdown and the specific-account picker are mutually exclusive — the parent clears one when the
 *  other is set. The empresa/consolidado scope comes from the topbar selector (`useApp`), not from a
 *  second control on the page. */
export function LedgerFilters({
  root,
  ledgerAccountId,
  from,
  to,
  costCenterId,
  unitId,
  onRootChange,
  onAccountChange,
  onFromChange,
  onToChange,
  onCostCenterChange,
  onUnitChange,
  onExport,
}: {
  root: string;
  ledgerAccountId: string;
  from: string;
  to: string;
  costCenterId: string;
  unitId: string;
  onRootChange: (v: string) => void;
  onAccountChange: (v: string) => void;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
  onCostCenterChange: (v: string) => void;
  onUnitChange: (v: string) => void;
  onExport: () => void;
}) {
  const { t } = useTranslation();
  const { scopeCompanyId } = useApp();
  const companyId = scopeCompanyId ?? '';
  const costCenters = useCostCenters(companyId);
  const units = useUnits(companyId);
  const accounts = useLedgerAccounts({ kind: 'Analytical', activeOnly: true });
  const accountItems = (accounts.data ?? []).map((a) => ({
    id: a.id,
    label: `${a.code} · ${a.name}`,
    sub: a.reducedCode ?? undefined,
  }));
  return (
    <div className='razao-filters'>
      <div className='razao-filters-row'>
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
        <ToolbarField label={t('accounting.generalLedger.filters.account')} className='razao-field-account'>
          <Combobox
            value={ledgerAccountId || null}
            onChange={(id) => onAccountChange(id ?? '')}
            items={accountItems}
            placeholder={t('accounting.generalLedger.filters.allAccounts')}
            searchPlaceholder={t('accounting.generalLedger.filters.accountSearch')}
            loading={accounts.isLoading}
          />
        </ToolbarField>
        <Button className='razao-export' icon={<Download />} onClick={onExport}>
          {t('accounting.generalLedger.export')}
        </Button>
      </div>
      <div className='razao-filters-row'>
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
      </div>
    </div>
  );
}
