import { useEffect, useState, type ReactNode } from 'react';
import {
  Building2, Network, Wallet, FolderTree, Settings, Search, Plus, ChevronRight,
  LayoutDashboard, Receipt, ClipboardCheck, TrendingUp, FileText,
} from 'lucide-react';
import { api, type Empresa, type Unidade, type CentroDeCusto } from './api';

function moeda(v: number) {
  return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 });
}
function iniciais(nome: string) {
  const p = nome.trim().split(/\s+/);
  return ((p[0]?.[0] ?? '') + (p[1]?.[0] ?? p[0]?.[1] ?? '')).toUpperCase();
}

export default function App() {
  const [empresas, setEmpresas] = useState<Empresa[]>([]);
  const [empresaSel, setEmpresaSel] = useState<Empresa | null>(null);
  const [unidades, setUnidades] = useState<Unidade[]>([]);
  const [centros, setCentros] = useState<CentroDeCusto[]>([]);

  const carregarEmpresas = () => api.listEmpresas().then(setEmpresas);
  useEffect(() => { carregarEmpresas().catch(() => {}); }, []);

  async function selecionarEmpresa(e: Empresa) {
    setEmpresaSel(e);
    const [us, cs] = await Promise.all([api.listUnidades(e.id), api.listCentros(e.id)]);
    setUnidades(us); setCentros(cs);
  }
  const recarregarEmpresa = () => empresaSel && selecionarEmpresa(empresaSel);

  return (
    <>
      <aside className="sidebar">
        <div className="sb-brand">
          <div className="brand-mark">U1</div>
          <div className="brand-text">ULBRA One<small>ERP · Grupo ULBRA</small></div>
        </div>

        <nav className="sb-nav">
          <NavItem icon={<LayoutDashboard />} label="Início" />
          <NavItem icon={<Receipt />} label="Lançamentos" />
          <NavItem icon={<ClipboardCheck />} label="Aprovações" />
          <NavItem icon={<TrendingUp />} label="Fluxo de caixa" />
          <NavItem icon={<FileText />} label="Relatórios" />
          <div className="sb-section">Estrutura</div>
          <NavItem icon={<FolderTree />} label="Cadastros" active />
          <NavItem icon={<Settings />} label="Configurações" />
        </nav>

        <div className="sb-foot">
          <div className="sb-avatar">FA</div>
          <div className="sb-foot-info">
            <div className="sb-foot-name">Felipe de Almeida</div>
            <div className="sb-foot-sub">Tecnologia &amp; Inovação</div>
          </div>
        </div>
      </aside>

      <main className="main">
        <header className="topbar">
          <div className="crumb">Cadastros <b>/ Estrutura organizacional</b></div>
          <div className="top-search"><Search /><input placeholder="Buscar…" /></div>
        </header>

        <div className="content">
          <div className="page-head">
            <h1 className="page-title">Cadastros</h1>
            <p className="page-sub">
              Estrutura do Grupo ULBRA. Cada empresa tem suas <b>unidades</b> e seus <b>centros de custo</b> —
              dimensões independentes: um centro de custo (ex.: Marketing) pode atuar em várias unidades.
            </p>
          </div>

          <div className="org-grid">
            {/* EMPRESAS */}
            <Coluna titulo="Empresas" icon={<Building2 />} count={empresas.length}>
              <CriarItem campos={[['razao', 'Razão social'], ['cnpj', 'CNPJ (14 dígitos)']]}
                onCriar={async (v) => { await api.createEmpresa(v.razao, v.cnpj); await carregarEmpresas(); }} />
              {empresas.map((e) => (
                <ItemOrg key={e.id} variant="" sigla={iniciais(e.razaoSocial)} titulo={e.razaoSocial}
                  sub={e.cnpj} ativo={empresaSel?.id === e.id} onClick={() => selecionarEmpresa(e)} navegavel />
              ))}
            </Coluna>

            {/* UNIDADES */}
            <Coluna titulo="Unidades" icon={<Network />} count={empresaSel ? unidades.length : undefined}
              vazio={!empresaSel ? 'Selecione uma empresa' : undefined}>
              {empresaSel && (
                <CriarItem campos={[['nome', 'Nome da unidade'], ['sigla', 'Sigla']]}
                  onCriar={async (v) => { await api.createUnidade(empresaSel.id, v.nome, v.sigla); await recarregarEmpresa(); }} />
              )}
              {unidades.map((u) => (
                <ItemOrg key={u.id} variant="alt" sigla={u.sigla.slice(0, 3) || iniciais(u.nome)} titulo={u.nome} sub={u.sigla} />
              ))}
            </Coluna>

            {/* CENTROS DE CUSTO */}
            <Coluna titulo="Centros de Custo" icon={<Wallet />} count={empresaSel ? centros.length : undefined}
              vazio={!empresaSel ? 'Selecione uma empresa' : undefined}>
              {empresaSel && (
                <CentroForm unidades={unidades}
                  onCriar={async (codigo, nome, orcamento, unidadeIds) => {
                    await api.createCentro(empresaSel.id, codigo, nome, orcamento, unidadeIds);
                    await recarregarEmpresa();
                  }} />
              )}
              {centros.map((c) => (
                <ItemOrg key={c.id} variant="alt2" sigla={c.codigo.slice(0, 3)} titulo={c.nome}
                  sub={`${moeda(c.orcamentoAnual)}/ano`}
                  tag={cobertura(c.unidadeIds.length, unidades.length)} />
              ))}
            </Coluna>
          </div>
        </div>
      </main>
    </>
  );
}

function cobertura(n: number, total: number) {
  if (n === 0) return 'sem unidade';
  if (total > 0 && n >= total) return 'todas as unidades';
  return n === 1 ? '1 unidade' : `${n} unidades`;
}

function NavItem({ icon, label, active }: { icon: ReactNode; label: string; active?: boolean }) {
  return (
    <button className={`sb-item ${active ? 'active' : 'disabled'}`} disabled={!active}>
      {icon} {label}
    </button>
  );
}

function Coluna({ titulo, icon, count, vazio, children }: {
  titulo: string; icon: ReactNode; count?: number; vazio?: string; children: ReactNode;
}) {
  return (
    <section className="org-col">
      <div className="org-col-head">
        {icon}<span className="org-col-title">{titulo}</span>
        {count !== undefined && <span className="org-col-count">{count}</span>}
      </div>
      {vazio ? <div className="empty"><Network />{vazio}</div> : <div className="org-list">{children}</div>}
    </section>
  );
}

function ItemOrg({ sigla, titulo, sub, tag, variant, ativo, navegavel, onClick }: {
  sigla: string; titulo: string; sub?: string; tag?: string; variant: '' | 'alt' | 'alt2';
  ativo?: boolean; navegavel?: boolean; onClick?: () => void;
}) {
  return (
    <button className={`org-item ${ativo ? 'active' : ''} ${onClick ? '' : 'static'}`} onClick={onClick}>
      <span className={`org-avatar ${variant}`}>{sigla}</span>
      <span className="org-item-body">
        <span className="org-item-title">{titulo}</span>
        {sub && <span className="org-item-sub">{sub}{tag && <>{' · '}<span className="cover-tag">{tag}</span></>}</span>}
      </span>
      {navegavel && <span className="org-item-chevron"><ChevronRight /></span>}
    </button>
  );
}

function CriarItem({ campos, onCriar }: { campos: [string, string][]; onCriar: (v: Record<string, string>) => Promise<void> }) {
  const [aberto, setAberto] = useState(false);
  const [valores, setValores] = useState<Record<string, string>>({});
  const [erro, setErro] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  if (!aberto) return <button className="add-btn" onClick={() => setAberto(true)}><Plus /> Adicionar</button>;

  return (
    <div className="create-form">
      {campos.map(([k, label], i) => (
        <input key={k} placeholder={label} value={valores[k] ?? ''} autoFocus={i === 0}
          onChange={(ev) => setValores((s) => ({ ...s, [k]: ev.target.value }))} />
      ))}
      {erro && <div className="create-err">{erro}</div>}
      <div className="create-row">
        <button className="btn btn-brand" disabled={salvando} onClick={async () => {
          setErro(null); setSalvando(true);
          try { await onCriar(valores); setValores({}); setAberto(false); }
          catch (e) { setErro(String(e)); } finally { setSalvando(false); }
        }}>Salvar</button>
        <button className="btn" onClick={() => { setAberto(false); setErro(null); }}>Cancelar</button>
      </div>
    </div>
  );
}

function CentroForm({ unidades, onCriar }: {
  unidades: Unidade[];
  onCriar: (codigo: string, nome: string, orcamento: number, unidadeIds: string[]) => Promise<void>;
}) {
  const [aberto, setAberto] = useState(false);
  const [codigo, setCodigo] = useState('');
  const [nome, setNome] = useState('');
  const [orcamento, setOrcamento] = useState('');
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [erro, setErro] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  const toggle = (id: string) => setSel((s) => {
    const n = new Set(s); n.has(id) ? n.delete(id) : n.add(id); return n;
  });
  const todas = unidades.length > 0 && sel.size === unidades.length;
  const toggleTodas = () => setSel(todas ? new Set() : new Set(unidades.map((u) => u.id)));

  if (!aberto) return <button className="add-btn" onClick={() => setAberto(true)}><Plus /> Adicionar</button>;

  return (
    <div className="create-form">
      <input placeholder="Código (ex.: MKT)" value={codigo} autoFocus onChange={(e) => setCodigo(e.target.value)} />
      <input placeholder="Nome (ex.: Marketing)" value={nome} onChange={(e) => setNome(e.target.value)} />
      <input placeholder="Orçamento anual (R$)" value={orcamento} inputMode="numeric" onChange={(e) => setOrcamento(e.target.value)} />
      <div className="field-label">Atua nas unidades</div>
      {unidades.length === 0
        ? <div className="uni-pick-empty">Cadastre uma unidade primeiro</div>
        : (
          <div className="uni-pick">
            <label className="uni-pick-row" style={{ fontWeight: 600 }}>
              <input type="checkbox" checked={todas} onChange={toggleTodas} /> Todas
            </label>
            {unidades.map((u) => (
              <label key={u.id} className="uni-pick-row">
                <input type="checkbox" checked={sel.has(u.id)} onChange={() => toggle(u.id)} /> {u.nome} <span style={{ color: 'var(--muted-foreground)' }}>({u.sigla})</span>
              </label>
            ))}
          </div>
        )}
      {erro && <div className="create-err">{erro}</div>}
      <div className="create-row">
        <button className="btn btn-brand" disabled={salvando} onClick={async () => {
          setErro(null); setSalvando(true);
          try {
            await onCriar(codigo, nome, Number(orcamento) || 0, [...sel]);
            setCodigo(''); setNome(''); setOrcamento(''); setSel(new Set()); setAberto(false);
          } catch (e) { setErro(String(e)); } finally { setSalvando(false); }
        }}>Salvar</button>
        <button className="btn" onClick={() => { setAberto(false); setErro(null); }}>Cancelar</button>
      </div>
    </div>
  );
}
