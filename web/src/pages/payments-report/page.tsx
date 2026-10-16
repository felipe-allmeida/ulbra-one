import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { FileText } from 'lucide-react';
import { useApp } from '../../store';
import { EmptyState, PageHeader } from '../../ui';
import { StageTabBar, type StageTab } from '../../components/workflow';
import { RelacaoTab } from './relacao-tab';
import { RemessasTitulosTab } from './remessas-titulos-tab';

const TAB_ORDER = ['relacao', 'remessas'] as const;
type TabKey = (typeof TAB_ORDER)[number];
const isTabKey = (v: string | null): v is TabKey => v != null && (TAB_ORDER as readonly string[]).includes(v);

/**
 * "Pagamentos Programados e Realizados" (AEL-958) — a página própria do Financeiro que porta a tela do
 * legado. Aba 1 = Relação de Pagamentos (filtros + Exportar PDF + Copiar Resumo); Aba 2 = Remessas /
 * Títulos (visualização do que foi gerado nas remessas CNAB, filtrável por unidade). A aba de remessas é
 * por-empresa → escondida na visão consolidada "Todas".
 */
export function PaymentsReportPage() {
  const { t } = useTranslation();
  const { empresaId, isAllCompanies, scopeReady } = useApp();
  const [params, setParams] = useSearchParams();

  const tabParam = params.get('tab');
  const rawActive: TabKey = isTabKey(tabParam) ? tabParam : 'relacao';
  const active: TabKey = isAllCompanies && rawActive === 'remessas' ? 'relacao' : rawActive;
  const setTab = (key: string) =>
    setParams((prev) => {
      prev.set('tab', key);
      prev.delete('page');
      return prev;
    });

  const tabKeys = isAllCompanies ? TAB_ORDER.filter((k) => k !== 'remessas') : TAB_ORDER;
  const tabs: StageTab[] = tabKeys.map((key) => ({ key, label: t(`paymentsReport.tabs.${key}`) }));

  return (
    <>
      <PageHeader title={t('paymentsReport.title')} sub={t('paymentsReport.subtitle')} />
      {!scopeReady ? (
        <EmptyState
          icon={<FileText />}
          title={t('paymentsReport.noCompanyTitle')}
          sub={t('paymentsReport.noCompanySub')}
        />
      ) : (
        <>
          <StageTabBar tabs={tabs} active={active} onChange={setTab} />
          {active === 'relacao' && <RelacaoTab companyId={empresaId} scopeReady={scopeReady} />}
          {active === 'remessas' && !isAllCompanies && <RemessasTitulosTab companyId={empresaId} />}
        </>
      )}
    </>
  );
}
