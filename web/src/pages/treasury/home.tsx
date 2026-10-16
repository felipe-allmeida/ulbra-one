import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { Search, Wallet } from 'lucide-react';
import { useApp } from '../../store';
import { usePaymentQueueSummary, usePaymentsHistory, useRemittances } from '../../queries';
import { EmptyState, PageHeader } from '../../ui';
import { RoleHome, StageTabBar, type StageTab } from '../../components/workflow';
import { PaymentQueueTab } from './payment-queue-tab';
import { PaymentsHistoryTab } from './history-tab';
import { TreasuryAside } from './treasury-aside';
import { RemittancesTab } from '../remessas/list';
import { CnabReturnTab } from './cnab-return-tab';

const TAB_ORDER = ['queue', 'remittances', 'cnabReturn', 'history'] as const;
type TreasuryTabKey = (typeof TAB_ORDER)[number];
const isTabKey = (v: string | null): v is TreasuryTabKey => v != null && (TAB_ORDER as readonly string[]).includes(v);

/**
 * Financeiro home — "Gestão Financeira" (the "home por papel" redesign, legacy screen parity):
 * payment queue + CNAB remittances + settlement history as tabs of one screen, in the RoleHome
 * shell with the payables-only cash-outflow aside. Conciliação bancária stays a SEPARATE screen
 * (/app/reconciliation) — its OFX import/matching flow is too dense for a tab; the aside's
 * "A conciliar" tile still points at it.
 * URL state: `?tab=` selects the tab; `?remittance=<id>` deep-links into a remittance's detail
 * (emitted by the queue's remittance chips and by pre-redesign links to /app/remittances).
 */
export function TreasuryHomePage() {
  const { t } = useTranslation();
  const { empresa, empresaId, isAllCompanies, scopeReady } = useApp();
  const [params, setParams] = useSearchParams();

  const tabParam = params.get('tab');
  const rawActive: TreasuryTabKey = isTabKey(tabParam) ? tabParam : 'queue';
  // Remittances (CNAB) and CNAB return are per-company; hide them in the consolidated "Todas" view (AEL-906).
  const perCompanyTabs: readonly string[] = ['remittances', 'cnabReturn'];
  const active: TreasuryTabKey = isAllCompanies && perCompanyTabs.includes(rawActive) ? 'queue' : rawActive;
  const setTab = (key: string) =>
    setParams((prev) => {
      prev.set('tab', key);
      // Tab-scoped params don't survive a tab switch.
      if (key !== 'remittances') prev.delete('remittance');
      prev.delete('page');
      return prev;
    });

  const focusRemittanceId = params.get('remittance');
  const clearFocus = () =>
    setParams((prev) => {
      prev.delete('remittance');
      return prev;
    });
  // A remittance deep link wins over the default tab (old /app/remittances?remittance=X links).
  useEffect(() => {
    if (focusRemittanceId && !isAllCompanies && active !== 'remittances') setTab('remittances');
    // eslint-disable-next-line react-hooks/exhaustive-deps -- run only when the deep link changes
  }, [focusRemittanceId]);

  // Free-text search (favorecido/CNPJ/documento) — debounced 250ms; applies to queue and history.
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(id);
  }, [search]);
  const searchable = active === 'queue' || active === 'history';

  // Tab counts reuse the same queries as the aside/tabs (react-query dedupes by key).
  const queueSummary = usePaymentQueueSummary(
    { companyId: empresaId, status: 'Scheduled', paid: false },
    { enabled: !!empresaId },
  ).data;
  const remittances = useRemittances(
    { companyId: isAllCompanies ? undefined : empresaId || undefined, page: 1, pageSize: 1 },
    { enabled: !!empresaId && !isAllCompanies },
  ).data;
  const history = usePaymentsHistory({ companyId: empresaId, page: 1, pageSize: 1 }, { enabled: !!empresaId }).data;

  const counts: Record<TreasuryTabKey, number | undefined> = {
    queue: queueSummary?.totalCount,
    remittances: remittances?.total,
    cnabReturn: undefined,
    history: history?.total,
  };
  // Remittances + CNAB return tabs are company-specific → dropped in the consolidated view.
  const tabKeys = isAllCompanies ? TAB_ORDER.filter((k) => k !== 'remittances' && k !== 'cnabReturn') : TAB_ORDER;
  const tabs: StageTab[] = tabKeys.map((key) => ({
    key,
    label: t(`treasury.home.tabs.${key}`),
    count: counts[key],
    badge: key === 'queue' && (queueSummary?.overdueCount ?? 0) > 0 ? 'danger' : undefined,
  }));

  return (
    <RoleHome aside={empresa ? <TreasuryAside companyId={empresaId} /> : undefined} className='treasury-role-home'>
      <PageHeader title={t('treasury.home.title')} sub={t('treasury.home.subtitle')}>
        {scopeReady && searchable && (
          <div className='top-search' style={{ width: 280 }}>
            <Search />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('treasury.home.searchPlaceholder')}
              aria-label={t('treasury.home.searchPlaceholder')}
            />
          </div>
        )}
      </PageHeader>
      {!scopeReady ? (
        <EmptyState icon={<Wallet />} title={t('treasury.home.noCompanyTitle')} sub={t('treasury.home.noCompanySub')} />
      ) : (
        <>
          <StageTabBar tabs={tabs} active={active} onChange={setTab} />
          {active === 'queue' && <PaymentQueueTab companyId={empresaId} search={debouncedSearch} />}
          {active === 'remittances' && !isAllCompanies && (
            <RemittancesTab companyId={empresaId} focusRemittanceId={focusRemittanceId} onClearFocus={clearFocus} />
          )}
          {active === 'cnabReturn' && !isAllCompanies && <CnabReturnTab companyId={empresaId} />}
          {active === 'history' && <PaymentsHistoryTab companyId={empresaId} search={debouncedSearch} />}
        </>
      )}
    </RoleHome>
  );
}
