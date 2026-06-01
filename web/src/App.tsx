import { useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  LayoutDashboard, ArrowUpRight, ArrowDownLeft, Activity, ClipboardCheck, Target,
  FileText, FileBox, LineChart, ShieldCheck, Building2, Plug, Settings,
  Users, Calculator, Package, Plus, Search, X,
} from 'lucide-react';
import { api, type Empresa, type Unidade, type CentroDeCusto } from './api';

const brl = (v: number) => v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 });
const brlCompact = (v: number) =>
  v >= 1_000_000 ? `R$ ${(v / 1_000_000).toLocaleString('pt-BR', { maximumFractionDigits: 2 })}M`
  : v >= 1_000 ? `R$ ${(v / 1_000).toLocaleString('pt-BR', { maximumFractionDigits: 0 })}K`
  : brl(v);

type Page = 'centros' | 'estrutura';
type Modal = null | 'empresa' | 'unidade' | 'centro';

export default function App() {
  const [page, setPage] = useState<Page>('centros');
  const [modal, setModal] = useState<Modal>(null);

  const [empresas, setEmpresas] = useState<Empresa[]>([]);
  const [empresaId, setEmpresaId] = useState<string>('');
  const [unidades, setUnidades] = useState<Unidade[]>([]);
  const [centros, setCentros] = useState<CentroDeCusto[]>([]);

  const empresa = empresas.find((e) => e.id === empresaId) ?? null;

  async function carregarEmpresas(selecionar?: string) {
    const es = await api.listEmpresas();
    setEmpresas(es);
    const id = selecionar ?? empresaId ?? '';
    const ok = es.find((e) => e.id === id) ? id : (es[0]?.id ?? '');
    if (ok !== empresaId) setEmpresaId(ok);
    return ok;
  }
  async function carregarEmpresa(id: string) {
    if (!id) { setUnidades([]); setCentros([]); return; }
    const [us, cs] = await Promise.all([api.listUnidades(id), api.listCentros(id)]);
    setUnidades(us); setCentros(cs);
  }

  useEffect(() => { carregarEmpresas().then((id) => carregarEmpresa(id)).catch(() => {}); }, []);
  useEffect(() => { carregarEmpresa(empresaId).catch(() => {}); }, [empresaId]);

  const unidadeSigla = useMemo(() => Object.fromEntries(unidades.map((u) => [u.id, u.sigla])), [unidades]);
  const orcamentoTotal = centros.reduce((s, c) => s + c.orcamentoAnual, 0);

  return (
    <>
      <aside className="sidebar">
        <div className="user-header">
          <div className="user-avatar-big">FA</div>
          <div className="user-header-info">
            <div className="user-header-name">Felipe de Almeida</div>
            <div className="user-header-role">Tecnologia &amp; Inovação</div>
          </div>
        </div>

        <nav className="sb-nav">
          <div className="sb-section">Financeiro</div>
          <Nav icon={<LayoutDashboard />} label="Dashboard" />
          <Nav icon={<ArrowUpRight />} label="Contas a pagar" />
          <Nav icon={<ArrowDownLeft />} label="Contas a receber" />
          <Nav icon={<Activity />} label="Fluxo de caixa" />
          <Nav icon={<ClipboardCheck />} label="Aprovações" />
          <Nav icon={<Target />} label="Centros de custo" active={page === 'centros'} onClick={() => setPage('centros')} />
          <Nav icon={<FileText />} label="Relatórios" />
          <Nav icon={<FileBox />} label="Documentos" />
          <Nav icon={<LineChart />} label="Projeções" />
          <Nav icon={<ShieldCheck />} label="Auditoria" />

          <div className="sb-section">Estrutura</div>
          <Nav icon={<Building2 />} label="Empresas e unidades" active={page === 'estrutura'} onClick={() => setPage('estrutura')} />

          <div className="sb-section">Sistema</div>
          <Nav icon={<Plug />} label="Integrações" />
          <Nav icon={<Settings />} label="Configurações" />

          <div className="sb-section">Módulos</div>
          <Nav icon={<Users />} label="RH" soon />
          <Nav icon={<Calculator />} label="Contábil" soon />
          <Nav icon={<Package />} label="Suprimentos" soon />
        </nav>
      </aside>

      <main className="main">
        <header className="topbar">
          <div className="crumb">
            {page === 'centros' ? <>Financeiro <b>/ Centros de custo</b></> : <>Estrutura <b>/ Empresas e unidades</b></>}
          </div>
          <div className="top-search"><Search /><input placeholder="Buscar…" /></div>
        </header>

        <div className="content">
          {/* Seletor de empresa comum às duas páginas */}
          {page === 'centros' && (
            <CentrosPage
              empresas={empresas} empresaId={empresaId} setEmpresaId={setEmpresaId}
              empresa={empresa} centros={centros} unidades={unidades}
              unidadeSigla={unidadeSigla} orcamentoTotal={orcamentoTotal}
              onNovo={() => setModal('centro')}
            />
          )}
          {page === 'estrutura' && (
            <EstruturaPage
              empresas={empresas} empresaId={empresaId} setEmpresaId={setEmpresaId}
              empresa={empresa} unidades={unidades}
              onNovaEmpresa={() => setModal('empresa')} onNovaUnidade={() => setModal('unidade')}
            />
          )}
        </div>
      </main>

      {modal === 'empresa' && (
        <EmpresaModal onClose={() => setModal(null)}
          onSalvar={async (razao, cnpj) => { const e = await api.createEmpresa(razao, cnpj); await carregarEmpresas(e.id); setModal(null); }} />
      )}
      {modal === 'unidade' && empresa && (
        <UnidadeModal empresa={empresa} onClose={() => setModal(null)}
          onSalvar={async (nome, sigla) => { await api.createUnidade(empresa.id, nome, sigla); await carregarEmpresa(empresa.id); setModal(null); }} />
      )}
      {modal === 'centro' && empresa && (
        <CentroModal empresa={empresa} unidades={unidades} onClose={() => setModal(null)}
          onSalvar={async (codigo, nome, orc, uids) => { await api.createCentro(empresa.id, codigo, nome, orc, uids); await carregarEmpresa(empresa.id); setModal(null); }} />
      )}
    </>
  );
}

/* ============ SIDEBAR ============ */
function Nav({ icon, label, active, soon, onClick }: { icon: ReactNode; label: string; active?: boolean; soon?: boolean; onClick?: () => void }) {
  const disabled = !active && !onClick;
  return (
    <button className={`sb-item ${active ? 'active' : ''} ${disabled ? 'disabled' : ''}`} disabled={disabled} onClick={onClick}>
      {icon} {label} {soon && <span className="soon">Em breve</span>}
    </button>
  );
}

/* ============ EMPRESA SELECTOR ============ */
function EmpresaSelect({ empresas, empresaId, setEmpresaId }: { empresas: Empresa[]; empresaId: string; setEmpresaId: (id: string) => void }) {
  return (
    <div className="field">
      <label>Empresa</label>
      <select className="select" value={empresaId} onChange={(e) => setEmpresaId(e.target.value)}>
        {empresas.length === 0 && <option value="">— nenhuma empresa —</option>}
        {empresas.map((e) => <option key={e.id} value={e.id}>{e.razaoSocial}</option>)}
      </select>
    </div>
  );
}

/* ============ PÁGINA: CENTROS DE CUSTO ============ */
function CentrosPage({ empresas, empresaId, setEmpresaId, empresa, centros, unidades, unidadeSigla, orcamentoTotal, onNovo }: {
  empresas: Empresa[]; empresaId: string; setEmpresaId: (id: string) => void; empresa: Empresa | null;
  centros: CentroDeCusto[]; unidades: Unidade[]; unidadeSigla: Record<string, string>; orcamentoTotal: number; onNovo: () => void;
}) {
  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-title">Centros de custo</h1>
          <p className="page-sub">Áreas funcionais da empresa. Um centro de custo (ex.: Marketing) pode atuar em várias unidades.</p>
        </div>
        <div className="toolbar">
          <EmpresaSelect empresas={empresas} empresaId={empresaId} setEmpresaId={setEmpresaId} />
          <button className="btn btn-brand" disabled={!empresa} onClick={onNovo}><Plus /> Novo centro de custo</button>
        </div>
      </div>

      {!empresa ? (
        <EmptyState titulo="Nenhuma empresa cadastrada" sub="Cadastre uma empresa em Estrutura para começar." />
      ) : (
        <>
          <div className="kpi-grid">
            <Kpi label="Centros de custo" value={String(centros.length)} />
            <Kpi label="Orçamento total (anual)" value={brlCompact(orcamentoTotal)} foot={`${centros.length} centro(s)`} />
            <Kpi label="Unidades" value={String(unidades.length)} foot={empresa.razaoSocial} />
          </div>

          {centros.length === 0 ? (
            <EmptyState titulo="Nenhum centro de custo" sub="Crie o primeiro centro de custo desta empresa." />
          ) : (
            <div className="cc-grid">
              {centros.map((c) => {
                const pct = orcamentoTotal > 0 ? Math.round((c.orcamentoAnual / orcamentoTotal) * 100) : 0;
                const todas = unidades.length > 0 && c.unidadeIds.length >= unidades.length;
                return (
                  <div className="cc-card" key={c.id}>
                    <div className="cc-card-top">
                      <div>
                        <div className="cc-name">{c.nome}</div>
                        <div className="cc-code">{c.codigo}</div>
                      </div>
                      <div className="cc-orc">
                        <div className="v">{brlCompact(c.orcamentoAnual)}</div>
                        <div className="l">orçamento/ano</div>
                      </div>
                    </div>
                    <div className="cc-progress"><div className="cc-progress-bar" style={{ width: `${pct}%` }} /></div>
                    <div className="cc-meta"><span>participação no orçamento</span><span>{pct}%</span></div>
                    <div className="chips">
                      {c.unidadeIds.length === 0 && <span className="chip">sem unidade</span>}
                      {todas
                        ? <span className="chip all">Todas as unidades</span>
                        : c.unidadeIds.map((id) => <span className="chip" key={id}>{unidadeSigla[id] ?? '—'}</span>)}
                    </div>
                  </div>
                );
              })}
              <button className="add-card" onClick={onNovo}><Plus /> Novo centro de custo</button>
            </div>
          )}
        </>
      )}
    </>
  );
}

/* ============ PÁGINA: ESTRUTURA ============ */
function EstruturaPage({ empresas, empresaId, setEmpresaId, empresa, unidades, onNovaEmpresa, onNovaUnidade }: {
  empresas: Empresa[]; empresaId: string; setEmpresaId: (id: string) => void; empresa: Empresa | null;
  unidades: Unidade[]; onNovaEmpresa: () => void; onNovaUnidade: () => void;
}) {
  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-title">Empresas e unidades</h1>
          <p className="page-sub">Pessoas jurídicas (CNPJ) do grupo e suas unidades (campus, polos).</p>
        </div>
      </div>

      <div className="panel" style={{ marginBottom: 16 }}>
        <div className="panel-head">
          <span className="panel-title">Empresas</span>
          <button className="btn btn-brand" onClick={onNovaEmpresa}><Plus /> Nova empresa</button>
        </div>
        <table>
          <thead><tr><th>Razão social</th><th>CNPJ</th><th>Status</th></tr></thead>
          <tbody>
            {empresas.length === 0
              ? <tr><td colSpan={3} className="t-empty">Nenhuma empresa cadastrada.</td></tr>
              : empresas.map((e) => (
                <tr key={e.id}>
                  <td>{e.razaoSocial}</td>
                  <td className="t-mono">{e.cnpj}</td>
                  <td>{e.ativa ? 'Ativa' : 'Inativa'}</td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>

      <div className="panel">
        <div className="panel-head">
          <div className="toolbar">
            <span className="panel-title">Unidades</span>
            <EmpresaSelect empresas={empresas} empresaId={empresaId} setEmpresaId={setEmpresaId} />
          </div>
          <button className="btn btn-brand" disabled={!empresa} onClick={onNovaUnidade}><Plus /> Nova unidade</button>
        </div>
        <table>
          <thead><tr><th>Nome</th><th>Sigla</th><th>Status</th></tr></thead>
          <tbody>
            {!empresa
              ? <tr><td colSpan={3} className="t-empty">Selecione uma empresa.</td></tr>
              : unidades.length === 0
                ? <tr><td colSpan={3} className="t-empty">Nenhuma unidade nesta empresa.</td></tr>
                : unidades.map((u) => (
                  <tr key={u.id}><td>{u.nome}</td><td className="t-mono">{u.sigla}</td><td>{u.ativa ? 'Ativa' : 'Inativa'}</td></tr>
                ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

/* ============ UI BITS ============ */
function Kpi({ label, value, foot }: { label: string; value: string; foot?: string }) {
  return <div className="kpi-card"><div className="kpi-label">{label}</div><div className="kpi-value">{value}</div>{foot && <div className="kpi-foot">{foot}</div>}</div>;
}
function EmptyState({ titulo, sub }: { titulo: string; sub: string }) {
  return <div className="empty-state"><Target /><div className="t">{titulo}</div><div className="s">{sub}</div></div>;
}

function Modal({ title, sub, children, onClose, onSalvar, salvando, podeSalvar }: {
  title: string; sub?: string; children: ReactNode; onClose: () => void; onSalvar: () => void; salvando: boolean; podeSalvar: boolean;
}) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
            <div><div className="modal-title">{title}</div>{sub && <div className="modal-sub">{sub}</div>}</div>
            <button className="btn" style={{ padding: 6, border: 'none' }} onClick={onClose}><X /></button>
          </div>
        </div>
        <div className="modal-body">{children}</div>
        <div className="modal-foot">
          <button className="btn" onClick={onClose}>Cancelar</button>
          <button className="btn btn-brand" disabled={!podeSalvar || salvando} onClick={onSalvar}>Salvar</button>
        </div>
      </div>
    </div>
  );
}

function useSalvar(fn: () => Promise<void>) {
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const run = async () => { setErro(null); setSalvando(true); try { await fn(); } catch (e) { setErro(String(e)); setSalvando(false); } };
  return { salvando, erro, run };
}

function EmpresaModal({ onClose, onSalvar }: { onClose: () => void; onSalvar: (razao: string, cnpj: string) => Promise<void> }) {
  const [razao, setRazao] = useState(''); const [cnpj, setCnpj] = useState('');
  const { salvando, erro, run } = useSalvar(() => onSalvar(razao.trim(), cnpj.trim()));
  return (
    <Modal title="Nova empresa" sub="Pessoa jurídica do grupo (CNPJ)" onClose={onClose} onSalvar={run} salvando={salvando} podeSalvar={!!razao.trim() && !!cnpj.trim()}>
      <div className="form-field"><label>Razão social</label><input className="input" autoFocus value={razao} onChange={(e) => setRazao(e.target.value)} /></div>
      <div className="form-field"><label>CNPJ (14 dígitos)</label><input className="input" value={cnpj} onChange={(e) => setCnpj(e.target.value)} /></div>
      {erro && <div className="create-err">{erro}</div>}
    </Modal>
  );
}

function UnidadeModal({ empresa, onClose, onSalvar }: { empresa: Empresa; onClose: () => void; onSalvar: (nome: string, sigla: string) => Promise<void> }) {
  const [nome, setNome] = useState(''); const [sigla, setSigla] = useState('');
  const { salvando, erro, run } = useSalvar(() => onSalvar(nome.trim(), sigla.trim()));
  return (
    <Modal title="Nova unidade" sub={empresa.razaoSocial} onClose={onClose} onSalvar={run} salvando={salvando} podeSalvar={!!nome.trim() && !!sigla.trim()}>
      <div className="form-field"><label>Nome</label><input className="input" autoFocus value={nome} onChange={(e) => setNome(e.target.value)} placeholder="ex.: Campus Canoas" /></div>
      <div className="form-field"><label>Sigla</label><input className="input" value={sigla} onChange={(e) => setSigla(e.target.value)} placeholder="ex.: CAN" /></div>
      {erro && <div className="create-err">{erro}</div>}
    </Modal>
  );
}

function CentroModal({ empresa, unidades, onClose, onSalvar }: {
  empresa: Empresa; unidades: Unidade[]; onClose: () => void;
  onSalvar: (codigo: string, nome: string, orc: number, uids: string[]) => Promise<void>;
}) {
  const [codigo, setCodigo] = useState(''); const [nome, setNome] = useState(''); const [orc, setOrc] = useState('');
  const [sel, setSel] = useState<Set<string>>(new Set());
  const todas = unidades.length > 0 && sel.size === unidades.length;
  const toggle = (id: string) => setSel((s) => { const n = new Set(s); n.has(id) ? n.delete(id) : n.add(id); return n; });
  const { salvando, erro, run } = useSalvar(() => onSalvar(codigo.trim(), nome.trim(), Number(orc) || 0, [...sel]));
  return (
    <Modal title="Novo centro de custo" sub={empresa.razaoSocial} onClose={onClose} onSalvar={run} salvando={salvando} podeSalvar={!!codigo.trim() && !!nome.trim()}>
      <div className="form-field"><label>Código</label><input className="input" autoFocus value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="ex.: MKT" /></div>
      <div className="form-field"><label>Nome</label><input className="input" value={nome} onChange={(e) => setNome(e.target.value)} placeholder="ex.: Marketing" /></div>
      <div className="form-field"><label>Orçamento anual (R$)</label><input className="input" value={orc} inputMode="numeric" onChange={(e) => setOrc(e.target.value)} placeholder="ex.: 500000" /></div>
      <div className="form-field">
        <label>Atua nas unidades</label>
        {unidades.length === 0
          ? <div className="uni-pick-empty">Cadastre uma unidade primeiro (em Estrutura).</div>
          : (
            <div className="uni-pick">
              <label className="uni-pick-row" style={{ fontWeight: 600 }}>
                <input type="checkbox" checked={todas} onChange={() => setSel(todas ? new Set() : new Set(unidades.map((u) => u.id)))} /> Todas as unidades
              </label>
              <div className="uni-pick-sep" />
              {unidades.map((u) => (
                <label className="uni-pick-row" key={u.id}>
                  <input type="checkbox" checked={sel.has(u.id)} onChange={() => toggle(u.id)} /> {u.nome} <span className="sig">{u.sigla}</span>
                </label>
              ))}
            </div>
          )}
      </div>
      {erro && <div className="create-err">{erro}</div>}
    </Modal>
  );
}
