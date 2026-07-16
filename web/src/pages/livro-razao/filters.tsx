import { useTranslation } from 'react-i18next';
import { Combobox, Input, Select, ToolbarField } from '../../ui';
import { ConsolidatedPicker } from '../contabil-shared';
import { useCostCenters, useLedgerAccounts } from '../../queries';

/** Company (consolidated) + account group (root 1–5) + specific-account search + competence range +
 *  cost center. The group dropdown and the specific-account picker are mutually exclusive — the parent
 *  clears one when the other is set. */
export function LedgerFilters({
  companyId,
  root,
  ledgerAccountId,
  from,
  to,
  costCenterId,
  onCompanyChange,
  onRootChange,
  onAccountChange,
  onFromChange,
  onToChange,
  onCostCenterChange,
}: {
  companyId: string;
  root: string;
  ledgerAccountId: string;
  from: string;
  to: string;
  costCenterId: string;
  onCompanyChange: (v: string) => void;
  onRootChange: (v: string) => void;
  onAccountChange: (v: string) => void;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
  onCostCenterChange: (v: string) => void;
}) {
  const { t } = useTranslation();
  const costCenters = useCostCenters(companyId);
  const accounts = useLedgerAccounts({ kind: 'Analytical', activeOnly: true });
  const accountItems = (accounts.data ?? []).map((a) => ({
    id: a.id,
    label: `${a.code} · ${a.name}`,
    sub: a.reducedCode ?? undefined,
  }));
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
      <ToolbarField label={t('accounting.generalLedger.filters.account')}>
        <Combobox
          value={ledgerAccountId || null}
          onChange={(id) => onAccountChange(id ?? '')}
          items={accountItems}
          placeholder={t('accounting.generalLedger.filters.allAccounts')}
          searchPlaceholder={t('accounting.generalLedger.filters.accountSearch')}
          loading={accounts.isLoading}
        />
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
