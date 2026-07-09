import { useTranslation } from 'react-i18next';
import { Calculator } from 'lucide-react';
import { useApp } from '../store';
import { PageHeader, EmptyState } from '../ui';
import { EmpresaPicker } from './shared';
import { AccountingTab } from './payables/accounting-tab';

/**
 * Contábil entry point for the accounting stage ("Registro de Despesa"): lists approved payables awaiting
 * accounting (PendingAccounting) and opens the expense review drawer (vendor tax, retentions, ledger
 * reclassify, NF, Contabilizar). Same worklist as Contas a pagar → Contabilidade, surfaced under the
 * Contábil menu as a dedicated page.
 */
export function ContabilDespesasPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();

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
        <AccountingTab companyId={empresaId} />
      )}
    </>
  );
}
