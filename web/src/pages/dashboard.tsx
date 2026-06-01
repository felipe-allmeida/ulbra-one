import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Building2, Network, Target, Wallet, Zap } from 'lucide-react';
import { api, type Unidade, type CentroDeCusto } from '../api';
import { useApp } from '../store';
import { brl, brlCompact } from '../lib';
import { Kpi, PageHeader, Panel, EmptyState } from '../ui';
import { EmpresaPicker } from './shared';

export function DashboardPage() {
  const { empresas, empresa, empresaId } = useApp();
  const nav = useNavigate();
  const [unidades, setUnidades] = useState<Unidade[]>([]);
  const [centros, setCentros] = useState<CentroDeCusto[]>([]);

  useEffect(() => {
    if (!empresaId) { setUnidades([]); setCentros([]); return; }
    Promise.all([api.listUnidades(empresaId), api.listCentros(empresaId)])
      .then(([u, c]) => { setUnidades(u); setCentros(c); }).catch(() => {});
  }, [empresaId]);

  const orcamentoTotal = centros.reduce((s, c) => s + c.orcamentoAnual, 0);
  const comAuto = centros.filter((c) => c.limiteAutoAprovacao > 0).length;
  const ordenados = [...centros].sort((a, b) => b.orcamentoAnual - a.orcamentoAnual);

  return (
    <>
      <PageHeader title="Início" sub="Visão geral da estrutura e do orçamento da empresa.">
        <EmpresaPicker />
      </PageHeader>

      {!empresa ? (
        <EmptyState icon={<Building2 />} title="Nenhuma empresa cadastrada" sub="Cadastre uma empresa em Estrutura para começar." />
      ) : (
        <>
          <div className="kpi-grid" style={{ gridTemplateColumns: 'repeat(4, 1fr)' }}>
            <Kpi label="Empresas (grupo)" value={String(empresas.length)} />
            <Kpi label="Unidades" value={String(unidades.length)} foot={empresa.razaoSocial} />
            <Kpi label="Centros de custo" value={String(centros.length)} foot={comAuto > 0 ? `${comAuto} com auto-aprovação` : undefined} />
            <Kpi label="Orçamento total (anual)" value={brlCompact(orcamentoTotal)} />
          </div>

          {centros.length === 0 ? (
            <EmptyState icon={<Target />} title="Nenhum centro de custo" sub="Cadastre centros de custo para ver a distribuição do orçamento." />
          ) : (
            <Panel title="Orçamento por centro de custo">
              {ordenados.map((c) => {
                const pct = orcamentoTotal > 0 ? Math.round((c.orcamentoAnual / orcamentoTotal) * 100) : 0;
                return (
                  <div className="bd-row" key={c.id} style={{ cursor: 'pointer' }} onClick={() => nav(`/centros/${c.id}`)}>
                    <div className="bd-name">{c.nome} <small>· {c.codigo}</small></div>
                    <div className="bd-bar"><div className="bd-fill" style={{ width: `${pct}%` }} /></div>
                    <div className="bd-val">{brl(c.orcamentoAnual)} <small style={{ color: 'var(--muted-foreground)', fontWeight: 400 }}>{pct}%</small></div>
                  </div>
                );
              })}
            </Panel>
          )}

          <div style={{ display: 'flex', gap: 10, marginTop: 16, flexWrap: 'wrap', color: 'var(--muted-foreground)', fontSize: 12.5 }}>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><Network size={14} /> {unidades.length} unidade(s)</span>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><Wallet size={14} /> {centros.length} centro(s) de custo</span>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><Zap size={14} /> {comAuto} com auto-aprovação</span>
          </div>
        </>
      )}
    </>
  );
}
