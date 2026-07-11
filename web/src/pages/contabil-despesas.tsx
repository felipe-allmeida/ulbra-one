import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { Calculator } from 'lucide-react';
import { useApp } from '../store';
import { PageHeader, EmptyState } from '../ui';
import { EmpresaPicker } from './shared';
import { StageTabBar, type StageTab } from '../components/workflow';
import { AccountingTab } from './payables/accounting-tab';
import { AdvancesTab } from './payables/advances-tab';

const TAB_ORDER = ['review', 'advances'] as const;
type DespesasTabKey = (typeof TAB_ORDER)[number];
const isTabKey = (v: string | null): v is DespesasTabKey =>
  v != null && (TAB_ORDER as readonly string[]).includes(v);

/**
 * Contábil entry point for the accounting stage ("Registro de Despesa"), in two tabs:
 * - Registro de Despesa: approved payables awaiting accounting (PendingAccounting) — opens the expense
 *   review drawer (vendor tax, retentions, ledger reclassify, NF, Contabilizar).
 * - Adiantamentos: every payable created as an advance (origin=Advance), whatever its stage — advances
 *   are paid before the NF exists, so the accountant tracks them here.
 */
export function ContabilDespesasPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const [params, setParams] = useSearchParams();

  const tabParam = params.get('tab');
  const active: DespesasTabKey = isTabKey(tabParam) ? tabParam : 'review';
  const setTab = (key: string) =>
    setParams((prev) => {
      prev.set('tab', key);
      return prev;
    });

  const tabs: StageTab[] = TAB_ORDER.map((key) => ({
    key,
    label: t(`expenseRecording.tabs.${key}`),
  }));

  return (
    <>
      <PageHeader title={t('expenseRecording.title')} sub={t('expenseRecording.subtitle')}>
        <EmpresaPicker />
      </PageHeader>
      {!empresa ? (
        <EmptyState
          icon={<Calculator />}
          title={t('expenseRecording.noCompanyTitle')}
          sub={t('expenseRecording.noCompanySub')}
        />
      ) : (
        <>
          <StageTabBar tabs={tabs} active={active} onChange={setTab} />
          {active === 'review' && <AccountingTab companyId={empresaId} />}
          {active === 'advances' && <AdvancesTab companyId={empresaId} />}
        </>
      )}
    </>
  );
}
