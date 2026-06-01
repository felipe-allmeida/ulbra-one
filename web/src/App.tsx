import { useEffect, useState, type ReactNode } from 'react';
import {
  Network, Building2, Wallet, Calculator, BarChart3, Compass,
  Plus, ChevronRight, Search, Settings, LifeBuoy,
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
  const [unidadeSel, setUnidadeSel] = useState<Unidade | null>(null);
  const [centros, setCentros] = useState<CentroDeCusto[]>([]);

  const carregarEmpresas = () => api.listEmpresas().then(setEmpresas);

  useEffect(() => { carregarEmpresas().catch(() => {}); }, []);

  async function selecionarEmpresa(e: Empresa) {
    setEmpresaSel(e); setUnidadeSel(null); setCentros([]);
    setUnidades(await api.listUnidades(e.id));
  }
  async function selecionarUnidade(u: Unidade) {
    setUnidadeSel(u);
    setCentros(await api.listCentros(u.id));
  }

  return (
    <>
      <aside className="sidebar">
        <div className="sb-brand">
          <div className="brand-mark">U1</div>
          <div className="brand-text">ULBRA One<small>ERP · Grupo ULBRA</small></div>
        </div>

        <nav className="sb-nav">
          <div className="sb-section">Principal</div>
          <button className="sb-item active"><Network /> Organograma</button>

          <div className="sb-section">Módulos</div>
          <button className="sb-item disabled" disabled><Wallet /> Financeiro <span className="soon">em breve</span></button>
          <button className="sb-item disabled" disabled><Calculator /> Contábil &amp; Fiscal <span className="soon">em breve</span></button>
          <button className="sb-item disabled" disabled><BarChart3 /> Gerencial <span className="soon">em breve</span></button>
          <button className="sb-item disabled" disabled><Compass /> Estratégico <span className="soon">em breve</span></button>

          <div className="sb-section">Sistema</div>
          <button className="sb-item disabled" disabled><Settings /> Configurações</button>
          <button className="sb-item disabled" disabled><LifeBuoy /> Suporte</button>
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
          <div className="crumb">Organograma <b>/ Estrutura organizacional</b></div>
          <div className="top-search">
            <Search />
            <input placeholder="Buscar…" />
          </div>
        </header>

        <div className="content">
          <div className="page-head">
            <h1 className="page-title">Organograma</h1>
            <p className="page-sub">Estrutura organizacional do Grupo ULBRA — empresas, unidades e centros de custo.</p>
          </div>

          <div className="org-grid">
            <Coluna titulo="Empresas" icon={<Building2 />} count={empresas.length}>
              <CriarItem campos={[['razao', 'Razão social'], ['cnpj', 'CNPJ (14 dígitos)']]}
                onCriar={async (v) => { await api.createEmpresa(v.razao, v.cnpj); await carregarEmpresas(); }} />
              {empresas.map((e) => (
                <ItemOrg key={e.id} variant="" sigla={iniciais(e.razaoSocial)} titulo={e.razaoSocial}
                  sub={e.cnpj} ativo={empresaSel?.id === e.id} onClick={() => selecionarEmpresa(e)} navegavel />
              ))}
            </Coluna>

            <Coluna titulo="Unidades" icon={<Network />} count={empresaSel ? unidades.length : undefined}
              vazio={!empresaSel ? 'Selecione uma empresa' : undefined}>
              {empresaSel && (
                <CriarItem campos={[['nome', 'Nome da unidade'], ['sigla', 'Sigla']]}
                  onCriar={async (v) => { await api.createUnidade(empresaSel.id, v.nome, v.sigla); setUnidades(await api.listUnidades(empresaSel.id)); }} />
              )}
              {unidades.map((u) => (
                <ItemOrg key={u.id} variant="alt" sigla={u.sigla.slice(0, 3) || iniciais(u.nome)} titulo={u.nome}
                  sub={u.sigla} ativo={unidadeSel?.id === u.id} onClick={() => selecionarUnidade(u)} navegavel />
              ))}
            </Coluna>

            <Coluna titulo="Centros de Custo" icon={<Wallet />} count={unidadeSel ? centros.length : undefined}
              vazio={!unidadeSel ? 'Selecione uma unidade' : undefined}>
              {unidadeSel && (
                <CriarItem campos={[['codigo', 'Código'], ['nome', 'Nome'], ['orcamento', 'Orçamento anual (R$)']]}
                  onCriar={async (v) => { await api.createCentro(unidadeSel.id, v.codigo, v.nome, Number(v.orcamento) || 0); setCentros(await api.listCentros(unidadeSel.id)); }} />
              )}
              {centros.map((c) => (
                <ItemOrg key={c.id} variant="alt2" sigla={c.codigo.slice(0, 3)} titulo={c.nome}
                  sub={`${c.codigo} · ${moeda(c.orcamentoAnual)}/ano`} />
              ))}
            </Coluna>
          </div>
        </div>
      </main>
    </>
  );
}

function Coluna({ titulo, icon, count, vazio, children }: {
  titulo: string; icon: ReactNode; count?: number; vazio?: string; children: ReactNode;
}) {
  return (
    <section className="org-col">
      <div className="org-col-head">
        {icon}
        <span className="org-col-title">{titulo}</span>
        {count !== undefined && <span className="org-col-count">{count}</span>}
      </div>
      {vazio
        ? <div className="empty"><Network />{vazio}</div>
        : <div className="org-list">{children}</div>}
    </section>
  );
}

function ItemOrg({ sigla, titulo, sub, variant, ativo, navegavel, onClick }: {
  sigla: string; titulo: string; sub?: string; variant: '' | 'alt' | 'alt2';
  ativo?: boolean; navegavel?: boolean; onClick?: () => void;
}) {
  return (
    <button className={`org-item ${ativo ? 'active' : ''} ${onClick ? '' : 'static'}`} onClick={onClick}>
      <span className={`org-avatar ${variant}`}>{sigla}</span>
      <span className="org-item-body">
        <span className="org-item-title">{titulo}</span>
        {sub && <span className="org-item-sub">{sub}</span>}
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

  if (!aberto) {
    return <button className="add-btn" onClick={() => setAberto(true)}><Plus /> Adicionar</button>;
  }

  return (
    <div className="create-form">
      {campos.map(([k, label]) => (
        <input key={k} placeholder={label} value={valores[k] ?? ''} autoFocus={k === campos[0][0]}
          onChange={(ev) => setValores((s) => ({ ...s, [k]: ev.target.value }))} />
      ))}
      {erro && <div className="create-err">{erro}</div>}
      <div className="create-row">
        <button className="btn btn-brand" disabled={salvando}
          onClick={async () => {
            setErro(null); setSalvando(true);
            try { await onCriar(valores); setValores({}); setAberto(false); }
            catch (e) { setErro(String(e)); }
            finally { setSalvando(false); }
          }}>Salvar</button>
        <button className="btn" onClick={() => { setAberto(false); setErro(null); }}>Cancelar</button>
      </div>
    </div>
  );
}
