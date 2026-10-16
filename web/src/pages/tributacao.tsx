import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Landmark, Search } from 'lucide-react';
import { useApp } from '../store';
import { PageHeader, EmptyState } from '../ui';
import { RoleHome } from '../components/workflow';
import { TaxAnalysisTab } from './payables/tax-analysis-tab';
import { TributacaoAside } from './payables/tributacao-aside';

/**
 * Tributação home: the tax-analysis queue (Serviço/Conta Fixa titles awaiting the Tributário's validation of
 * their withholdings, which blocks payment + accounting). This is the former Contábil "Despesas" drawer, extracted
 * into its own role/page. Gated by payables:tax at the route.
 */
export function TributacaoPage() {
  const { t } = useTranslation();
  const { empresaId, isAllCompanies, scopeReady } = useApp();

  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(id);
  }, [search]);

  return (
    <RoleHome aside={scopeReady ? <TributacaoAside companyId={empresaId} /> : undefined}>
      <PageHeader title={t('tax.title')} sub={t('tax.subtitle')}>
        {scopeReady && (
          <div className='top-search' style={{ width: 260 }}>
            <Search />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('tax.searchPlaceholder')}
              aria-label={t('tax.searchPlaceholder')}
            />
          </div>
        )}
      </PageHeader>
      {!scopeReady ? (
        <EmptyState icon={<Landmark />} title={t('tax.noCompanyTitle')} sub={t('tax.noCompanySub')} />
      ) : (
        <TaxAnalysisTab companyId={empresaId} search={debouncedSearch} isAllCompanies={isAllCompanies} />
      )}
    </RoleHome>
  );
}
