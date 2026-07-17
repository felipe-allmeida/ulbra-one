import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ArrowLeftRight } from 'lucide-react';
import { type BankTransaction, type MatchStatus, type TransactionDirection } from '../../api';
import { useApp } from '../../store';
import { useBankAccounts, useBankTransactions, useImportStatement, usePeriodSummary } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { EmptyState, Panel, Select, ToolbarField } from '../../ui';
import { P, usePermissions } from '../../permissions';
import { EmpresaPicker } from '../shared';
import { ImportPanel } from './import-panel';
import { PeriodSummaryTiles } from './period-summary';
import { TransactionsTable } from './transactions-table';
import { ManualMatchDrawer } from './manual-match-drawer';

const STATUS_FILTERS: MatchStatus[] = ['Suggested', 'Unmatched', 'Reconciled', 'Ignored'];

/** Conciliação bancária: import an OFX statement, see the period's charges by status, and reconcile. */
export function ConciliacaoPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const toast = useToast();
  const canWrite = usePermissions().can(P.treasuryWrite);

  const accounts = useBankAccounts(empresaId).data ?? [];
  const [bankAccountId, setBankAccountId] = useState('');
  const [status, setStatus] = useState<MatchStatus | ''>('');
  const [direction, setDirection] = useState<TransactionDirection | ''>('');
  const [dueFrom, setDueFrom] = useState('');
  const [dueTo, setDueTo] = useState('');
  const [page, setPage] = useState(1);
  const [manualTx, setManualTx] = useState<BankTransaction | null>(null);
  const [warning, setWarning] = useState<string | null>(null);

  // Reset selection when the company changes (keep a valid bank account).
  useEffect(() => {
    setBankAccountId((cur) => (accounts.find((a) => a.id === cur) ? cur : (accounts[0]?.id ?? '')));
  }, [accounts]);
  useEffect(() => setPage(1), [bankAccountId, status, direction, dueFrom, dueTo]);

  const importStatement = useImportStatement({
    onSuccess: (imp) => {
      toast.success(
        t('conciliacao.summary.counts', {
          total: imp.transactionCount,
          newCount: imp.newCount,
          duplicateCount: imp.duplicateCount,
        }),
      );
      // Non-blocking account-mismatch notice, shown as a banner under the import panel.
      setWarning(imp.warning ?? null);
      // Default the period filter to the imported statement's period so the user lands on "this import".
      if (imp.periodStart) setDueFrom(imp.periodStart);
      if (imp.periodEnd) setDueTo(imp.periodEnd);
    },
    onError: (e) => {
      toast.error(getErrorMessage(e));
      setWarning(null);
    },
  });

  const periodFilters = {
    companyId: empresaId,
    bankAccountId,
    direction: direction || undefined,
    dueFrom: dueFrom || undefined,
    dueTo: dueTo || undefined,
  };
  const enabled = !!empresaId && !!bankAccountId;
  const summary = usePeriodSummary(periodFilters, { enabled });
  const list = useBankTransactions({ ...periodFilters, matchStatus: status || undefined, page }, { enabled });
  const items = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  const onFile = (file: File | null) => {
    if (!file || !bankAccountId || importStatement.isPending) return;
    setWarning(null);
    importStatement.mutate({ bankAccountId, file });
  };

  return (
    <>
      <div className='page-head'>
        <div>
          <h1 className='page-title'>{t('conciliacao.title')}</h1>
          <p className='page-sub'>{t('conciliacao.subtitle')}</p>
        </div>
        <div className='toolbar'>
          <EmpresaPicker />
          <ToolbarField label={t('conciliacao.bankAccount')}>
            <Select value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)}>
              <option value=''>{t('conciliacao.bankAccountSelect')}</option>
              {accounts.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.alias}
                </option>
              ))}
            </Select>
          </ToolbarField>
        </div>
      </div>

      {!empresa ? (
        <EmptyState
          icon={<ArrowLeftRight />}
          title={t('conciliacao.empty.noCompanyTitle')}
          sub={t('conciliacao.empty.noCompanySub')}
        />
      ) : (
        <>
          {canWrite && (
            <ImportPanel
              hasBankAccount={!!bankAccountId}
              importing={importStatement.isPending}
              warning={warning}
              onSelect={onFile}
            />
          )}

          {!bankAccountId ? (
            <EmptyState
              icon={<ArrowLeftRight />}
              title={t('conciliacao.empty.noBankAccountTitle')}
              sub={t('conciliacao.empty.noBankAccountSub')}
            />
          ) : (
            <>
              <PeriodSummaryTiles summary={summary.data} />
              <Panel
                title={t('conciliacao.periodTitle')}
                action={
                  <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
                    <ToolbarField label={t('conciliacao.filter.from')}>
                      <input type='date' className='input' value={dueFrom} onChange={(e) => setDueFrom(e.target.value)} />
                    </ToolbarField>
                    <ToolbarField label={t('conciliacao.filter.to')}>
                      <input type='date' className='input' value={dueTo} onChange={(e) => setDueTo(e.target.value)} />
                    </ToolbarField>
                    <ToolbarField label={t('conciliacao.filter.direction')}>
                      <Select value={direction} onChange={(e) => setDirection(e.target.value as TransactionDirection | '')}>
                        <option value=''>{t('conciliacao.filter.all')}</option>
                        <option value='Debit'>{t('conciliacao.direction.Debit')}</option>
                        <option value='Credit'>{t('conciliacao.direction.Credit')}</option>
                      </Select>
                    </ToolbarField>
                    <ToolbarField label={t('conciliacao.filter.label')}>
                      <Select value={status} onChange={(e) => setStatus(e.target.value as MatchStatus | '')}>
                        <option value=''>{t('conciliacao.filter.all')}</option>
                        {STATUS_FILTERS.map((s) => (
                          <option key={s} value={s}>
                            {t(`conciliacao.status.${s}`)}
                          </option>
                        ))}
                      </Select>
                    </ToolbarField>
                  </div>
                }
              >
                <TransactionsTable
                  items={items}
                  page={page}
                  totalPages={totalPages}
                  onPage={setPage}
                  onManual={setManualTx}
                />
              </Panel>
            </>
          )}
        </>
      )}

      {manualTx && <ManualMatchDrawer companyId={empresaId} transaction={manualTx} onClose={() => setManualTx(null)} />}
    </>
  );
}
