import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { Calculator, Search } from 'lucide-react';
import { useApp } from '../store';
import { usePayableStageItems } from '../queries';
import { PageHeader, EmptyState } from '../ui';
import { RoleHome, StageTabBar, type StageTab } from '../components/workflow';
import { AdvancesTab } from './payables/advances-tab';
import { ReviewPendingTab } from './payables/review-pending-tab';
import { ContabilAside } from './payables/contabil-aside';

const TAB_ORDER = ['reviewPending', 'advances'] as const;
type ReviewTabKey = (typeof TAB_ORDER)[number];
const isTabKey = (v: string | null): v is ReviewTabKey => v != null && (TAB_ORDER as readonly string[]).includes(v);

/**
 * Contábil home — "Revisão de Contabilidade" (the "home por papel" redesign). Unifies the old redundant
 * surfaces (Registro de Despesa + Pendentes de contabilização + Classificação) into a single per-role
 * screen, in the RoleHome shell with the "A caminho do Financeiro" aside:
 * - Revisão de classificação (default): requisition payables auto-scheduled without a ledger account —
 *   AI suggestion + Classificar/Contabilizar (the "review when the Payable is generated" flow).
 * - Adiantamentos: advance-origin payables.
 * The old "Despesas" tab (fiscal review + withholdings) moved out to the Tributação role/page.
 */
export function ContabilRevisaoPage() {
  const { t } = useTranslation();
  const { empresaId, isAllCompanies, scopeReady } = useApp();
  const [params, setParams] = useSearchParams();

  const tabParam = params.get('tab');
  const active: ReviewTabKey = isTabKey(tabParam) ? tabParam : 'reviewPending';
  const setTab = (key: string) =>
    setParams((prev) => {
      prev.set('tab', key);
      return prev;
    });

  // Free-text search (queryable) — debounced 250ms so it doesn't hit the query on every keystroke.
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(id);
  }, [search]);

  // Tab counts reuse the same endpoints as the aside (react-query dedupes by query key).
  const reviewPending = usePayableStageItems(
    { companyId: empresaId, reviewPending: true, page: 1, pageSize: 1 },
    { enabled: !!empresaId },
  ).data;

  const counts: Record<ReviewTabKey, number | undefined> = {
    reviewPending: reviewPending?.total,
    advances: undefined,
  };
  const tabs: StageTab[] = TAB_ORDER.map((key) => ({
    key,
    label: t(`accounting.review.tabs.${key}`),
    count: counts[key],
    badge: key === 'reviewPending' && (counts[key] ?? 0) > 0 ? 'warning' : undefined,
  }));

  return (
    <RoleHome aside={scopeReady ? <ContabilAside companyId={empresaId} /> : undefined}>
      <PageHeader title={t('accounting.review.title')} sub={t('accounting.review.subtitle')}>
        {scopeReady && (
          <div className='top-search' style={{ width: 260 }}>
            <Search />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('accounting.review.searchPlaceholder')}
              aria-label={t('accounting.review.searchPlaceholder')}
            />
          </div>
        )}
      </PageHeader>
      {!scopeReady ? (
        <EmptyState
          icon={<Calculator />}
          title={t('accounting.review.noCompanyTitle')}
          sub={t('accounting.review.noCompanySub')}
        />
      ) : (
        <>
          <StageTabBar tabs={tabs} active={active} onChange={setTab} />
          {active === 'reviewPending' && (
            <ReviewPendingTab companyId={empresaId} search={debouncedSearch} isAllCompanies={isAllCompanies} />
          )}
          {active === 'advances' && (
            <AdvancesTab companyId={empresaId} search={debouncedSearch} isAllCompanies={isAllCompanies} />
          )}
        </>
      )}
    </RoleHome>
  );
}
