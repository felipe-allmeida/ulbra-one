import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Tags, Check, Lightbulb } from 'lucide-react';
import { api, type ChartAccount, type PayableListItem } from '../api';
import { useApp } from '../store';
import { useChartAccounts, useClassifyBatch, useClassifyPayable, usePayables } from '../queries';
import { brl } from '../lib';
import { getErrorMessage } from '../apiError';
import { useToast } from '../toast';
import { Button, Combobox, EmptyState, Pager, Panel } from '../ui';
import { EmpresaPicker } from './shared';
import { fmtData } from '../datetime';

/** Builds the `code — name` option list of analytical accounts for a chart type. */
function useAccountItems(accounts: ChartAccount[]) {
  return useMemo(
    () =>
      [...accounts]
        .sort((a, b) => a.code.localeCompare(b.code))
        .map((a) => ({ id: a.id, label: `${a.code} — ${a.name}` })),
    [accounts],
  );
}

/* ============ Per-row classify cell ============ */
function RowClassify({
  payableId,
  accountingItems,
  financialItems,
  accountsLoading,
}: {
  payableId: string;
  accountingItems: { id: string; label: string }[];
  financialItems: { id: string; label: string }[];
  accountsLoading: boolean;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const [accountingAccountId, setAccountingAccountId] = useState<string | null>(null);
  const [financialAccountId, setFinancialAccountId] = useState<string | null>(null);
  const [suggesting, setSuggesting] = useState(false);

  const classify = useClassifyPayable({
    onSuccess: () => toast.success(t('classification.classified')),
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const onSuggest = async () => {
    setSuggesting(true);
    try {
      const s = await api.classificationSuggestion(payableId);
      if (!s.accountingAccountId && !s.financialAccountId) {
        toast.error(t('classification.noSuggestion'));
        return;
      }
      if (s.accountingAccountId) setAccountingAccountId(s.accountingAccountId);
      if (s.financialAccountId) setFinancialAccountId(s.financialAccountId);
      toast.success(t('classification.suggestionApplied'));
    } catch (e) {
      toast.error(getErrorMessage(e));
    } finally {
      setSuggesting(false);
    }
  };

  // Classify is a full REPLACE — always send BOTH ids together.
  const canClassify = !!accountingAccountId && !!financialAccountId && !classify.isPending;

  return (
    <div style={{ display: 'flex', gap: 8, alignItems: 'flex-start', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
      <div style={{ minWidth: 200 }}>
        <Combobox
          value={accountingAccountId}
          onChange={setAccountingAccountId}
          items={accountingItems}
          loading={accountsLoading}
          placeholder={t('classification.selectAccount')}
          searchPlaceholder={t('classification.searchAccount')}
          emptyText={t('classification.emptyAccounts')}
        />
      </div>
      <div style={{ minWidth: 200 }}>
        <Combobox
          value={financialAccountId}
          onChange={setFinancialAccountId}
          items={financialItems}
          loading={accountsLoading}
          placeholder={t('classification.selectAccount')}
          searchPlaceholder={t('classification.searchAccount')}
          emptyText={t('classification.emptyAccounts')}
        />
      </div>
      <Button icon={<Lightbulb />} disabled={suggesting} onClick={() => void onSuggest()}>
        {t('classification.suggest')}
      </Button>
      <Button
        variant='brand'
        icon={<Check />}
        disabled={!canClassify}
        onClick={() => classify.mutate({ id: payableId, body: { accountingAccountId, financialAccountId } })}
      >
        {classify.isPending ? t('classification.classifying') : t('classification.classify')}
      </Button>
    </div>
  );
}

/* ============ Bulk bar ============ */
function BulkBar({
  selectedIds,
  accountingItems,
  financialItems,
  accountsLoading,
  onClear,
}: {
  selectedIds: string[];
  accountingItems: { id: string; label: string }[];
  financialItems: { id: string; label: string }[];
  accountsLoading: boolean;
  onClear: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const [accountingAccountId, setAccountingAccountId] = useState<string | null>(null);
  const [financialAccountId, setFinancialAccountId] = useState<string | null>(null);

  const batch = useClassifyBatch({
    onSuccess: (res) => {
      toast.success(t('classification.bulk.result', { count: res.classifiedCount }));
      setAccountingAccountId(null);
      setFinancialAccountId(null);
      onClear();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const canApply = !!accountingAccountId && !!financialAccountId && selectedIds.length > 0 && !batch.isPending;

  return (
    <div
      style={{
        display: 'flex',
        gap: 12,
        alignItems: 'center',
        flexWrap: 'wrap',
        padding: '12px 16px',
        marginBottom: 14,
        borderRadius: 12,
        background: 'var(--panel, #fff)',
        border: '1px solid var(--border, #e5e7eb)',
        position: 'sticky',
        top: 12,
        zIndex: 5,
      }}
    >
      <strong>{t('classification.bulk.selected', { count: selectedIds.length })}</strong>
      <div style={{ minWidth: 220 }}>
        <Combobox
          value={accountingAccountId}
          onChange={setAccountingAccountId}
          items={accountingItems}
          loading={accountsLoading}
          placeholder={t('classification.bulk.accountingAccount')}
          searchPlaceholder={t('classification.searchAccount')}
          emptyText={t('classification.emptyAccounts')}
        />
      </div>
      <div style={{ minWidth: 220 }}>
        <Combobox
          value={financialAccountId}
          onChange={setFinancialAccountId}
          items={financialItems}
          loading={accountsLoading}
          placeholder={t('classification.bulk.financialAccount')}
          searchPlaceholder={t('classification.searchAccount')}
          emptyText={t('classification.emptyAccounts')}
        />
      </div>
      <Button
        variant='brand'
        icon={<Check />}
        disabled={!canApply}
        onClick={() => batch.mutate({ payableIds: selectedIds, accountingAccountId, financialAccountId })}
      >
        {batch.isPending ? t('classification.bulk.applying') : t('classification.bulk.apply')}
      </Button>
      <Button onClick={onClear}>{t('classification.bulk.clear')}</Button>
    </div>
  );
}

/* ============ Page ============ */
export function ClassificacaoPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const accounting = useChartAccounts(
    { chartType: 'Accounting', kind: 'Analytical', activeOnly: true },
    { enabled: !!empresaId },
  );
  const financial = useChartAccounts(
    { chartType: 'Financial', kind: 'Analytical', activeOnly: true },
    { enabled: !!empresaId },
  );
  const accountingItems = useAccountItems(accounting.data ?? []);
  const financialItems = useAccountItems(financial.data ?? []);
  const accountsLoading = accounting.isLoading || financial.isLoading;

  const list = usePayables({ companyId: empresaId, classified: false, page }, { enabled: !!empresaId });
  const totalPages = list.data?.totalPages ?? 1;

  useEffect(() => {
    setPage(1);
    setSelected(new Set());
  }, [empresaId]);

  // The fila lists per-installment rows; classification is per payable. Dedupe by payableId.
  const payables = useMemo(() => {
    const seen = new Map<string, PayableListItem>();
    for (const it of list.data?.items ?? []) if (!seen.has(it.payableId)) seen.set(it.payableId, it);
    return [...seen.values()];
  }, [list.data]);

  const toggle = (payableId: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(payableId)) next.delete(payableId);
      else next.add(payableId);
      return next;
    });
  const allSelected = payables.length > 0 && payables.every((p) => selected.has(p.payableId));
  const toggleAll = () =>
    setSelected((prev) => (allSelected ? new Set() : new Set([...prev, ...payables.map((p) => p.payableId)])));

  const selectedIds = [...selected];

  return (
    <>
      <div className='page-head'>
        <div>
          <h1 className='page-title'>{t('classification.title')}</h1>
          <p className='page-sub'>{t('classification.subtitle')}</p>
        </div>
        <div className='toolbar'>
          <EmpresaPicker />
        </div>
      </div>

      {!empresa ? (
        <EmptyState
          icon={<Tags />}
          title={t('classification.empty.noCompanyTitle')}
          sub={t('classification.empty.noCompanySub')}
        />
      ) : payables.length === 0 ? (
        <EmptyState
          icon={<Tags />}
          title={t('classification.empty.doneTitle')}
          sub={t('classification.empty.doneSub')}
        />
      ) : (
        <>
          {selectedIds.length > 0 && (
            <BulkBar
              selectedIds={selectedIds}
              accountingItems={accountingItems}
              financialItems={financialItems}
              accountsLoading={accountsLoading}
              onClear={() => setSelected(new Set())}
            />
          )}

          <Panel title={t('classification.count', { count: list.data?.total ?? payables.length })}>
            <table>
              <thead>
                <tr>
                  <th style={{ width: 36 }}>
                    <input
                      type='checkbox'
                      aria-label={t('classification.bulk.apply')}
                      checked={allSelected}
                      onChange={toggleAll}
                    />
                  </th>
                  <th>{t('classification.columns.vendor')}</th>
                  <th>{t('classification.columns.description')}</th>
                  <th style={{ textAlign: 'right' }}>{t('classification.columns.amount')}</th>
                  <th>{t('classification.columns.dueDate')}</th>
                  <th style={{ textAlign: 'right' }}>{t('classification.columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {payables.map((p) => (
                  <tr key={p.payableId}>
                    <td>
                      <input
                        type='checkbox'
                        aria-label={p.counterpartyName || p.description}
                        checked={selected.has(p.payableId)}
                        onChange={() => toggle(p.payableId)}
                      />
                    </td>
                    <td>{p.counterpartyName || '—'}</td>
                    <td>{p.description}</td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {brl(p.amount)}
                    </td>
                    <td className='t-mono'>{fmtData(p.dueDate)}</td>
                    <td className='td-action'>
                      <RowClassify
                        payableId={p.payableId}
                        accountingItems={accountingItems}
                        financialItems={financialItems}
                        accountsLoading={accountsLoading}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager page={page} totalPages={totalPages} onPage={setPage} />
          </Panel>
        </>
      )}
    </>
  );
}
