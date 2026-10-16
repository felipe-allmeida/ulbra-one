import { Building2, Network, Target, Wallet, Zap } from 'lucide-react';
import { useApp } from '../store';
import { useCostCenters, useUnits } from '../queries';
import { brl } from '../lib';
import { useOriginNav } from '../nav';
import { Kpi, PageHeader, Panel, EmptyState, LoadingState } from '../ui';

export function DashboardPage() {
  const { empresas, empresa, empresaId, isAllCompanies, scopeReady } = useApp();
  const openDetail = useOriginNav();
  const unitsQuery = useUnits(empresaId);
  const unidades = unitsQuery.data ?? [];
  const ccQuery = useCostCenters(empresaId);
  const centros = ccQuery.data ?? [];

  const budgetTotal = centros.reduce((s, c) => s + c.annualBudget, 0);
  const comAuto = centros.filter((c) => c.autoApprovalLimit > 0).length;
  const ordenados = [...centros].sort((a, b) => b.annualBudget - a.annualBudget);

  return (
    <>
      <PageHeader title='Início' sub='Visão geral da estrutura e do orçamento da empresa.'></PageHeader>

      {!scopeReady ? (
        <EmptyState
          icon={<Building2 />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : unitsQuery.isLoading || ccQuery.isLoading ? (
        <LoadingState />
      ) : (
        <>
          <div className='kpi-grid' style={{ gridTemplateColumns: 'repeat(4, 1fr)' }}>
            <Kpi label='Empresas (grupo)' value={String(empresas.length)} />
            <Kpi
              label='Unidades'
              value={String(unidades.length)}
              foot={isAllCompanies ? 'Todas as empresas' : empresa?.legalName}
            />
            <Kpi
              label='Naturezas orçamentárias'
              value={String(centros.length)}
              foot={comAuto > 0 ? `${comAuto} com auto-aprovação` : undefined}
            />
            <Kpi label='Orçamento total (anual)' value={brl(budgetTotal)} />
          </div>

          {centros.length === 0 ? (
            <EmptyState
              icon={<Target />}
              title='Nenhuma natureza orçamentária'
              sub='Cadastre naturezas orçamentárias para ver a distribuição do orçamento.'
            />
          ) : (
            <Panel title='Orçamento por natureza'>
              {ordenados.map((c) => {
                const pct = budgetTotal > 0 ? Math.round((c.annualBudget / budgetTotal) * 100) : 0;
                return (
                  <div
                    className='bd-row'
                    key={c.id}
                    style={{ cursor: 'pointer' }}
                    onClick={() => openDetail(`/app/cost-centers/${c.id}`)}
                  >
                    <div className='bd-name'>
                      {c.name} <small>· {c.code}</small>
                    </div>
                    <div className='bd-bar'>
                      <div className='bd-fill' style={{ width: `${pct}%` }} />
                    </div>
                    <div className='bd-val'>
                      {brl(c.annualBudget)}{' '}
                      <small style={{ color: 'var(--muted-foreground)', fontWeight: 400 }}>{pct}%</small>
                    </div>
                  </div>
                );
              })}
            </Panel>
          )}

          <div
            style={{
              display: 'flex',
              gap: 10,
              marginTop: 16,
              flexWrap: 'wrap',
              color: 'var(--muted-foreground)',
              fontSize: 12.5,
            }}
          >
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              <Network size={14} /> {unidades.length} unidade(s)
            </span>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              <Wallet size={14} /> {centros.length} centro(s) de custo
            </span>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              <Zap size={14} /> {comAuto} com auto-aprovação
            </span>
          </div>
        </>
      )}
    </>
  );
}
