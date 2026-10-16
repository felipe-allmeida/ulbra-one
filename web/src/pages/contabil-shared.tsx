import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Download } from 'lucide-react';
import { useCostCenters, useUnits } from '../queries';
import { useApp } from '../store';
import { Button, Input, Select, ToolbarField } from '../ui';

/**
 * Shared filter toolbar for the accounting report screens (DRE, Balancete, …): competence range +
 * unidade + centro de custo, plus an optional CSV export button. Rendered as children of `PageHeader`, so
 * the filters sit inline in the header's `.toolbar` on a single row (see docs/frontend-conventions.md
 * "Report headers").
 *
 * The empresa/consolidado scope is NOT a page filter: it comes from the single selector in the topbar
 * (`useApp`), so every screen follows the same scope and the page never shows a second company control.
 * Unidade and centro de custo are company-scoped, so they stay disabled in the consolidated
 * ("Todas as empresas") view; the routed page remounts when the company changes (`KeyedOutlet` in
 * App.tsx), which clears them on its own.
 */
export function ReportToolbar({
  from,
  to,
  costCenterId,
  unitId,
  onFromChange,
  onToChange,
  onCostCenterChange,
  onUnitChange,
  onExport,
}: {
  from: string;
  to: string;
  costCenterId: string;
  unitId: string;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
  onCostCenterChange: (v: string) => void;
  onUnitChange: (v: string) => void;
  onExport?: () => void;
}) {
  const { t } = useTranslation();
  const { scopeCompanyId } = useApp();
  const companyId = scopeCompanyId ?? '';
  const costCenters = useCostCenters(companyId);
  // Units are company-scoped: within the selected company the user can drill into one unit (estabelecimento).
  const units = useUnits(companyId);
  return (
    <>
      <ToolbarField label={t('accounting.journal.filters.from')}>
        <Input type='date' value={from} max={to || undefined} onChange={(e) => onFromChange(e.target.value)} />
      </ToolbarField>
      <ToolbarField label={t('accounting.journal.filters.to')}>
        <Input type='date' value={to} min={from || undefined} onChange={(e) => onToChange(e.target.value)} />
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
      {onExport && (
        <Button className='razao-export' icon={<Download />} onClick={onExport}>
          {t('accounting.exportCsv')}
        </Button>
      )}
    </>
  );
}

/**
 * Standard header for the accounting report screens (DRE, Balancete, Livro Diário, …): the page title +
 * subtitle stacked in their own block, and the filter fields on a row **below** (never crammed onto the same
 * line as the title). `children` are the filter fields as a fragment (e.g. `ReportToolbar` / `BookFilters`),
 * laid out in the shared `.razao-filters-row`. Keeps every report header identical and prevents the
 * one-line-too-crowded layout — see docs/frontend-conventions.md "Report headers".
 */
export function ReportHead({ title, sub, children }: { title: string; sub?: string; children: ReactNode }) {
  return (
    <div className='razao-head'>
      <div>
        <h1 className='page-title'>{title}</h1>
        {sub && <p className='page-sub'>{sub}</p>}
      </div>
      <div className='razao-filters-row'>{children}</div>
    </div>
  );
}
