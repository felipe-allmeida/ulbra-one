import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Plus, Target, ChevronLeft, Pencil, Activity, Zap } from 'lucide-react';
import { api, type Unidade, type CentroDeCusto } from '../api';
import { useApp } from '../store';
import { brlCompact, brl } from '../lib';
import { Button, Chip, Kpi, EmptyState, PageHeader } from '../ui';
import { EmpresaPicker, CentroForm } from './shared';

/* ===================== LISTA ===================== */
export function CentrosListPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const [unidades, setUnidades] = useState<Unidade[]>([]);
  const [centros, setCentros] = useState<CentroDeCusto[]>([]);

  useEffect(() => {
    if (!empresaId) { setUnidades([]); setCentros([]); return; }
    Promise.all([api.listUnidades(empresaId), api.listCentros(empresaId)])
      .then(([u, c]) => { setUnidades(u); setCentros(c); }).catch(() => {});
  }, [empresaId]);

  const unidadeSigla = useMemo(() => Object.fromEntries(unidades.map((u) => [u.id, u.sigla])), [unidades]);
  const orcamentoTotal = centros.reduce((s, c) => s + c.orcamentoAnual, 0);

  return (
    <>
      <PageHeader title="Centros de custo" sub="Áreas funcionais da empresa. Um centro de custo (ex.: Marketing) pode atuar em várias unidades.">
        <EmpresaPicker />
        <Button variant="brand" icon={<Plus />} disabled={!empresa} onClick={() => nav('/centros/novo')}>Novo centro de custo</Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState icon={<Target />} title="Nenhuma empresa cadastrada" sub="Cadastre uma empresa em Estrutura para começar." />
      ) : (
        <>
          <div className="kpi-grid">
            <Kpi label="Centros de custo" value={String(centros.length)} />
            <Kpi label="Orçamento total (anual)" value={brlCompact(orcamentoTotal)} foot={`${centros.length} centro(s)`} />
            <Kpi label="Unidades" value={String(unidades.length)} foot={empresa.razaoSocial} />
          </div>

          {centros.length === 0 ? (
            <EmptyState icon={<Target />} title="Nenhum centro de custo" sub="Crie o primeiro centro de custo desta empresa." />
          ) : (
            <div className="cc-grid">
              {centros.map((c) => {
                const pct = orcamentoTotal > 0 ? Math.round((c.orcamentoAnual / orcamentoTotal) * 100) : 0;
                const todas = unidades.length > 0 && c.unidadeIds.length >= unidades.length;
                return (
                  <div className="cc-card" key={c.id} onClick={() => nav(`/centros/${c.id}`)} title="Abrir">
                    <div className="cc-card-top">
                      <div><div className="cc-name">{c.nome}</div><div className="cc-code">{c.codigo}</div></div>
                      <div className="cc-orc"><div className="v">{brlCompact(c.orcamentoAnual)}</div><div className="l">orçamento/ano</div></div>
                    </div>
                    <div className="cc-progress"><div className="cc-progress-bar" style={{ width: `${pct}%` }} /></div>
                    <div className="cc-meta"><span>participação no orçamento</span><span>{pct}%</span></div>
                    <div className="chips">
                      {c.unidadeIds.length === 0 && <Chip>sem unidade</Chip>}
                      {todas ? <Chip tone="brand">Todas as unidades</Chip> : c.unidadeIds.map((id) => <Chip key={id}>{unidadeSigla[id] ?? '—'}</Chip>)}
                      {c.limiteAutoAprovacao > 0 && <Chip tone="auto" icon={<Zap />}>auto ≤ {brlCompact(c.limiteAutoAprovacao)}</Chip>}
                    </div>
                  </div>
                );
              })}
              <button className="add-card" onClick={() => nav('/centros/novo')}><Plus /> Novo centro de custo</button>
            </div>
          )}
        </>
      )}
    </>
  );
}

/* ===================== DETALHE / NOVO ===================== */
export function CentroDetailPage({ modo }: { modo: 'novo' | 'detalhe' }) {
  const { id } = useParams();
  const nav = useNavigate();
  const { empresa, empresas } = useApp();

  const [centro, setCentro] = useState<CentroDeCusto | null>(null);
  const [unidades, setUnidades] = useState<Unidade[]>([]);
  const [editando, setEditando] = useState(modo === 'novo');
  const [carregando, setCarregando] = useState(modo === 'detalhe');

  // empresa do centro (no detalhe) ou empresa selecionada (no novo)
  const empresaDoCentro = centro ? empresas.find((e) => e.id === centro.empresaId) ?? null : empresa;

  useEffect(() => {
    if (modo === 'novo') {
      if (empresa) api.listUnidades(empresa.id).then(setUnidades).catch(() => {});
      return;
    }
    if (!id) return;
    setCarregando(true);
    api.getCentro(id)
      .then(async (c) => { setCentro(c); setUnidades(await api.listUnidades(c.empresaId)); })
      .catch(() => setCentro(null))
      .finally(() => setCarregando(false));
  }, [id, modo, empresa?.id]);

  /* ---- NOVO ---- */
  if (modo === 'novo') {
    return (
      <>
        <button className="backlink" onClick={() => nav('/centros')}><ChevronLeft /> Centros de custo</button>
        <div className="detail-head"><div><h1 className="detail-title">Novo centro de custo</h1>
          <div className="detail-sub">{empresa?.razaoSocial ?? 'Selecione uma empresa'}</div></div></div>
        {empresa
          ? <div className="card-form"><CentroForm empresa={empresa} unidades={unidades}
              onSaved={(cc) => nav(`/centros/${cc.id}`)} onCancel={() => nav('/centros')} /></div>
          : <EmptyState icon={<Target />} title="Nenhuma empresa" sub="Cadastre uma empresa primeiro." />}
      </>
    );
  }

  /* ---- DETALHE ---- */
  if (carregando) return <div className="empty-state">Carregando…</div>;
  if (!centro || !empresaDoCentro) return <EmptyState icon={<Target />} title="Centro de custo não encontrado" sub="Ele pode ter sido removido." />;

  const todas = unidades.length > 0 && centro.unidadeIds.length >= unidades.length;
  const cobertura = todas ? 'Todas as unidades'
    : centro.unidadeIds.length === 0 ? 'Nenhuma unidade'
    : centro.unidadeIds.map((id) => unidades.find((u) => u.id === id)?.sigla ?? '—').join(', ');

  return (
    <>
      <button className="backlink" onClick={() => nav('/centros')}><ChevronLeft /> Centros de custo</button>
      <div className="detail-head">
        <div>
          <h1 className="detail-title"><Target size={20} /> {centro.nome}</h1>
          <div className="detail-sub">{centro.codigo} · {empresaDoCentro.razaoSocial}</div>
        </div>
        {!editando && <div className="detail-actions"><Button icon={<Pencil />} onClick={() => setEditando(true)}>Editar</Button></div>}
      </div>

      {editando ? (
        <div className="card-form">
          <CentroForm empresa={empresaDoCentro} unidades={unidades} initial={centro}
            onSaved={(cc) => { setCentro(cc); setEditando(false); }} onCancel={() => setEditando(false)} />
        </div>
      ) : (
        <>
          <div className="info-grid">
            <Info label="Orçamento anual" value={brl(centro.orcamentoAnual)} />
            <Info label="Auto-aprovação" value={centro.limiteAutoAprovacao > 0 ? `até ${brl(centro.limiteAutoAprovacao)}` : 'desativada'} />
            <Info label="Cobertura" value={cobertura} />
          </div>

          <h2 className="section-title">Atividade</h2>
          <div className="activity-empty">
            <Activity />
            Os lançamentos (contas a pagar/receber) deste centro de custo aparecerão aqui quando o módulo financeiro estiver ativo.
          </div>
        </>
      )}
    </>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return <div className="info-card"><div className="info-label">{label}</div><div className="info-value">{value}</div></div>;
}
