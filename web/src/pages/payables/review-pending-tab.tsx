import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePayableStageItems, useLedgerAccounts } from '../../queries';
import { useApp } from '../../store';
import { P, usePermissions } from '../../permissions';
import { PayableStageList } from './payable-stage-list';
import { ClassificationDrawer } from './classification-drawer';

/**
 * "Revisão de classificação" tab of the Contábil home (AEL-854/865): requisition payables that
 * auto-scheduled WITHOUT a ledger account (the AI candidate is only a suggestion) — their accounting
 * incurral is parked until the accountant classifies them here. Compact expandable list (PayableStageList):
 * each row expands to the suggested account + a "Classificar" button opening the ClassificationDrawer
 * (full payable context: retentions, NF, account picker).
 */
export function ReviewPendingTab({
  companyId,
  search,
  isAllCompanies,
}: {
  companyId: string;
  search?: string;
  isAllCompanies?: boolean;
}) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const { empresas } = useApp();
  // Read-only in the consolidated "Todas" view (AEL-906): act after switching to a company.
  const canWrite = can(P.payablesWrite) && !isAllCompanies;
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId, search]);

  const list = usePayableStageItems({ companyId, reviewPending: true, search, page }, { enabled: !!companyId });
  const data = list.data;
  const accountsData = useLedgerAccounts({ kind: 'Analytical', activeOnly: true }).data;
  const accountLabel = useMemo(() => {
    const byId = new Map((accountsData ?? []).map((a) => [a.id, `${a.code} — ${a.name}`]));
    return (id?: string | null) => (id ? byId.get(id) : undefined);
  }, [accountsData]);

  const [classifyId, setClassifyId] = useState<string | null>(null);

  return (
    <>
      <PayableStageList
        items={data?.items ?? []}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        loading={list.isLoading}
        emptyTitle={t('expenseRecording.reviewPending.emptyTitle')}
        emptySub={t('expenseRecording.reviewPending.emptySub')}
        companyNameFor={isAllCompanies ? (item) => empresas.find((e) => e.id === item.companyId)?.legalName : undefined}
        // Entering is the whole job of this screen — expanding first only added a click.
        onEnter={canWrite ? (item) => setClassifyId(item.payableId) : undefined}
        // The suggested account used to sit in the expanded body; with the row entering on click it has to be
        // on the line itself, or the operator loses the one hint that makes the queue skimmable.
        // Suggestion + confidence used to live in the expanded body; the row enters on click now, so they have
        // to be on the line itself or the queue stops being skimmable.
        metaExtra={(item) => {
          const account = accountLabel(item.suggestedAccountingAccountId ?? item.accountingAccountId);
          if (!account) return t('expenseRecording.reviewPending.noSuggestionHint');
          const conf = item.classificationConfidence;
          return conf != null ? `${account} (${Math.round(conf * 100)}%)` : account;
        }}
        flagFor={(item) =>
          item.hasFiscalDocument === false && item.origin !== 'Advance'
            ? { text: t('payables.tags.fiscalDocPending'), tone: 'warn' }
            : undefined
        }
      />
      {classifyId && <ClassificationDrawer payableId={classifyId} onClose={() => setClassifyId(null)} />}
    </>
  );
}
