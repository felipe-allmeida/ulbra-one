import { useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  LayoutDashboard, ArrowUpRight, ArrowDownLeft, Activity, ClipboardCheck, Target,
  FileText, FileBox, LineChart, ShieldCheck, Building2, Plug, Settings,
  Users, Calculator, Package, Plus, Search, Menu, Pencil, Zap,
} from 'lucide-react';
import { api, type Empresa, type Unidade, type CentroDeCusto } from './api';
import { brlCompact } from './lib';
import {
  Button, Input, Select, Field, ToolbarField, Chip, Kpi, EmptyState, PageHeader, Panel, Modal, useSave,
} from './ui';

type Page = 'centros' | 'estrutura';
type ModalState =
  | null
  | { kind: 'empresa'; editing?: Empresa }
  | { kind: 'unidade'; editing?: Unidade }
  | { kind: 'centro'; editing?: CentroDeCusto };

export default function App() {
  const [page, setPage] = useState<Page>('centros');
  const [navOpen, setNavOpen] = useState(false);
  const [modal, setModal] = useState<ModalState>(null);

  const [empresas, setEmpresas] = useState<Empresa[]>([]);
  const [empresaId, setEmpresaId] = useState('');
  const [unidades, setUnidades] = useState<Unidade[]>([]);
  const [centros, setCentros] = useState<CentroDeCusto[]>([]);

  const empresa = empresas.find((e) => e.id === empresaId) ?? null;

  async function carregarEmpresas(selecionar?: string) {
    const es = await api.listEmpresas();
    setEmpresas(es);
    const id = selecionar ?? empresaId;
    const ok = es.find((e) => e.id === id) ? id : (es[0]?.id ?? '');
    setEmpresaId(ok);
    return ok;
  }
  async function carregarEmpresa(id: string) {
    if (!id) { setUnidades([]); setCentros([]); return; }
    const [us, cs] = await Promise.all([api.listUnidades(id), api.listCentros(id)]);
    setUnidades(us); setCentros(cs);
  }

  useEffect(() => { carregarEmpresas().then(carregarEmpresa).catch(() => {}); }, []);
  useEffect(() => { carregarEmpresa(empresaId).catch(() => {}); }, [empresaId]);

  const navigate = (p: Page) => { setPage(p); setNavOpen(false); };
  const empresaSelect = (
    <ToolbarField label="Empresa">
      <Select value={empresaId} onChange={(e) => setEmpresaId(e.target.value)}>
        {empresas.length === 0 && <option value="">— nenhuma —</option>}
        {empresas.map((e) => <option key={e.id} value={e.id}>{e.razaoSocial}</option>)}
      </Select>
    </ToolbarField>
  );

  return (
    <>
      {navOpen && <div className="sb-backdrop show" onClick={() => setNavOpen(false)} />}
      <aside className={`sidebar ${navOpen ? 'open' : ''}`}>
        <div className="user-header">
          <div className="user-avatar-big">FA</div>
          <div className="user-header-info">
            <div className="user-header-name">Felipe de Almeida</div>
            <div className="user-header-role">Tecnologia &amp; Inovação</div>
          </div>
        </div>
        <nav className="sb-nav">
          <Section>Financeiro</Section>
          <NavItem icon={<LayoutDashboard />} label="Dashboard" />
          <NavItem icon={<ArrowUpRight />} label="Contas a pagar" />
          <NavItem icon={<ArrowDownLeft />} label="Contas a receber" />
          <NavItem icon={<Activity />} label="Fluxo de caixa" />
          <NavItem icon={<ClipboardCheck />} label="Aprovações" />
          <NavItem icon={<Target />} label="Centros de custo" active={page === 'centros'} onClick={() => navigate('centros')} />
          <NavItem icon={<FileText />} label="Relatórios" />
          <NavItem icon={<FileBox />} label="Documentos" />
          <NavItem icon={<LineChart />} label="Projeções" />
          <NavItem icon={<ShieldCheck />} label="Auditoria" />
          <Section>Estrutura</Section>
          <NavItem icon={<Building2 />} label="Empresas e unidades" active={page === 'estrutura'} onClick={() => navigate('estrutura')} />
          <Section>Sistema</Section>
          <NavItem icon={<Plug />} label="Integrações" />
          <NavItem icon={<Settings />} label="Configurações" />
          <Section>Módulos</Section>
          <NavItem icon={<Users />} label="RH" soon />
          <NavItem icon={<Calculator />} label="Contábil" soon />
          <NavItem icon={<Package />} label="Suprimentos" soon />
        </nav>
      </aside>

      <main className="main">
        <header className="topbar">
          <div className="topbar-menu">
            <button className="icon-btn hamburger" onClick={() => setNavOpen(true)} aria-label="Menu"><Menu /></button>
            <div className="crumb">
              {page === 'centros' ? <>Financeiro <b>/ Centros de custo</b></> : <>Estrutura <b>/ Empresas e unidades</b></>}
            </div>
          </div>
          <div className="top-search"><Search /><input placeholder="Buscar…" /></div>
        </header>

        <div className="content">
          {page === 'centros' ? (
            <CentrosPage
              empresa={empresa} centros={centros} unidades={unidades} select={empresaSelect}
              onNovo={() => setModal({ kind: 'centro' })}
              onEditar={(c) => setModal({ kind: 'centro', editing: c })}
            />
          ) : (
            <EstruturaPage
              empresas={empresas} empresa={empresa} unidades={unidades} select={empresaSelect}
              onNovaEmpresa={() => setModal({ kind: 'empresa' })}
              onEditarEmpresa={(e) => setModal({ kind: 'empresa', editing: e })}
              onNovaUnidade={() => setModal({ kind: 'unidade' })}
              onEditarUnidade={(u) => setModal({ kind: 'unidade', editing: u })}
            />
          )}
        </div>
      </main>

      {modal?.kind === 'empresa' && (
        <EmpresaModal editing={modal.editing} onClose={() => setModal(null)}
          onDone={async (id) => { await carregarEmpresas(id); setModal(null); }} />
      )}
      {modal?.kind === 'unidade' && empresa && (
        <UnidadeModal empresa={empresa} editing={modal.editing} onClose={() => setModal(null)}
          onDone={async () => { await carregarEmpresa(empresa.id); setModal(null); }} />
      )}
      {modal?.kind === 'centro' && empresa && (
        <CentroModal empresa={empresa} unidades={unidades} editing={modal.editing} onClose={() => setModal(null)}
          onDone={async () => { await carregarEmpresa(empresa.id); setModal(null); }} />
      )}
    </>
  );
}

/* ============ SIDEBAR ============ */
const Section = ({ children }: { children: ReactNode }) => <div className="sb-section">{children}</div>;
function NavItem({ icon, label, active, soon, onClick }: { icon: ReactNode; label: string; active?: boolean; soon?: boolean; onClick?: () => void }) {
  const disabled = !active && !onClick;
  return (
    <button className={`sb-item ${active ? 'active' : ''} ${disabled ? 'disabled' : ''}`} disabled={disabled} onClick={onClick}>
      {icon} {label} {soon && <span className="soon">Em breve</span>}
    </button>
  );
}

/* ============ CENTROS DE CUSTO ============ */
function CentrosPage({ empresa, centros, unidades, select, onNovo, onEditar }: {
  empresa: Empresa | null; centros: CentroDeCusto[]; unidades: Unidade[]; select: ReactNode;
  onNovo: () => void; onEditar: (c: CentroDeCusto) => void;
}) {
  const unidadeSigla = useMemo(() => Object.fromEntries(unidades.map((u) => [u.id, u.sigla])), [unidades]);
  const orcamentoTotal = centros.reduce((s, c) => s + c.orcamentoAnual, 0);

  return (
    <>
      <PageHeader title="Centros de custo" sub="Áreas funcionais da empresa. Um centro de custo (ex.: Marketing) pode atuar em várias unidades.">
        {select}
        <Button variant="brand" icon={<Plus />} disabled={!empresa} onClick={onNovo}>Novo centro de custo</Button>
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
                  <div className="cc-card" key={c.id} onClick={() => onEditar(c)} title="Editar">
                    <div className="cc-card-top">
                      <div>
                        <div className="cc-name">{c.nome}</div>
                        <div className="cc-code">{c.codigo}</div>
                      </div>
                      <div className="cc-orc"><div className="v">{brlCompact(c.orcamentoAnual)}</div><div className="l">orçamento/ano</div></div>
                    </div>
                    <div className="cc-progress"><div className="cc-progress-bar" style={{ width: `${pct}%` }} /></div>
                    <div className="cc-meta"><span>participação no orçamento</span><span>{pct}%</span></div>
                    <div className="chips">
                      {c.unidadeIds.length === 0 && <Chip>sem unidade</Chip>}
                      {todas ? <Chip tone="brand">Todas as unidades</Chip>
                        : c.unidadeIds.map((id) => <Chip key={id}>{unidadeSigla[id] ?? '—'}</Chip>)}
                      {c.limiteAutoAprovacao > 0 && <Chip tone="auto" icon={<Zap />}>auto ≤ {brlCompact(c.limiteAutoAprovacao)}</Chip>}
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

/* ============ ESTRUTURA ============ */
function EstruturaPage({ empresas, empresa, unidades, select, onNovaEmpresa, onEditarEmpresa, onNovaUnidade, onEditarUnidade }: {
  empresas: Empresa[]; empresa: Empresa | null; unidades: Unidade[]; select: ReactNode;
  onNovaEmpresa: () => void; onEditarEmpresa: (e: Empresa) => void; onNovaUnidade: () => void; onEditarUnidade: (u: Unidade) => void;
}) {
  return (
    <>
      <PageHeader title="Empresas e unidades" sub="Pessoas jurídicas (CNPJ) do grupo e suas unidades (campus, polos)." />

      <div style={{ marginBottom: 16 }}>
        <Panel title="Empresas" action={<Button variant="brand" icon={<Plus />} onClick={onNovaEmpresa}>Nova empresa</Button>}>
          <table>
            <thead><tr><th>Razão social</th><th>CNPJ</th><th>Status</th><th className="td-action"></th></tr></thead>
            <tbody>
              {empresas.length === 0
                ? <tr><td colSpan={4} className="t-empty">Nenhuma empresa cadastrada.</td></tr>
                : empresas.map((e) => (
                  <tr key={e.id}>
                    <td>{e.razaoSocial}</td>
                    <td className="t-mono">{e.cnpj}</td>
                    <td>{e.ativa ? 'Ativa' : 'Inativa'}</td>
                    <td className="td-action"><button className="icon-btn" onClick={() => onEditarEmpresa(e)} aria-label="Editar"><Pencil /></button></td>
                  </tr>
                ))}
            </tbody>
          </table>
        </Panel>
      </div>

      <Panel title="Unidades"
        action={<div className="toolbar">{select}<Button variant="brand" icon={<Plus />} disabled={!empresa} onClick={onNovaUnidade}>Nova unidade</Button></div>}>
        <table>
          <thead><tr><th>Nome</th><th>Sigla</th><th>Status</th><th className="td-action"></th></tr></thead>
          <tbody>
            {!empresa
              ? <tr><td colSpan={4} className="t-empty">Selecione uma empresa.</td></tr>
              : unidades.length === 0
                ? <tr><td colSpan={4} className="t-empty">Nenhuma unidade nesta empresa.</td></tr>
                : unidades.map((u) => (
                  <tr key={u.id}>
                    <td>{u.nome}</td><td className="t-mono">{u.sigla}</td><td>{u.ativa ? 'Ativa' : 'Inativa'}</td>
                    <td className="td-action"><button className="icon-btn" onClick={() => onEditarUnidade(u)} aria-label="Editar"><Pencil /></button></td>
                  </tr>
                ))}
          </tbody>
        </table>
      </Panel>
    </>
  );
}

/* ============ MODAIS ============ */
function ModalFooter({ onClose, onSalvar, salvando, podeSalvar }: { onClose: () => void; onSalvar: () => void; salvando: boolean; podeSalvar: boolean }) {
  return (<><Button onClick={onClose}>Cancelar</Button><Button variant="brand" disabled={!podeSalvar || salvando} onClick={onSalvar}>{salvando ? 'Salvando…' : 'Salvar'}</Button></>);
}

function EmpresaModal({ editing, onClose, onDone }: { editing?: Empresa; onClose: () => void; onDone: (id: string) => Promise<void> }) {
  const [razao, setRazao] = useState(editing?.razaoSocial ?? '');
  const [cnpj, setCnpj] = useState(editing?.cnpj ?? '');
  const { salvando, erro, run } = useSave(async () => {
    const e = editing ? await api.updateEmpresa(editing.id, razao.trim(), cnpj.trim()) : await api.createEmpresa(razao.trim(), cnpj.trim());
    await onDone(e.id);
  });
  return (
    <Modal title={editing ? 'Editar empresa' : 'Nova empresa'} sub="Pessoa jurídica do grupo (CNPJ)" onClose={onClose}
      footer={<ModalFooter onClose={onClose} onSalvar={run} salvando={salvando} podeSalvar={!!razao.trim() && !!cnpj.trim()} />}>
      <Field label="Razão social"><Input autoFocus value={razao} onChange={(e) => setRazao(e.target.value)} /></Field>
      <Field label="CNPJ (14 dígitos)"><Input value={cnpj} onChange={(e) => setCnpj(e.target.value)} /></Field>
      {erro && <div className="create-err">{erro}</div>}
    </Modal>
  );
}

function UnidadeModal({ empresa, editing, onClose, onDone }: { empresa: Empresa; editing?: Unidade; onClose: () => void; onDone: () => Promise<void> }) {
  const [nome, setNome] = useState(editing?.nome ?? '');
  const [sigla, setSigla] = useState(editing?.sigla ?? '');
  const { salvando, erro, run } = useSave(async () => {
    editing ? await api.updateUnidade(editing.id, nome.trim(), sigla.trim()) : await api.createUnidade(empresa.id, nome.trim(), sigla.trim());
    await onDone();
  });
  return (
    <Modal title={editing ? 'Editar unidade' : 'Nova unidade'} sub={empresa.razaoSocial} onClose={onClose}
      footer={<ModalFooter onClose={onClose} onSalvar={run} salvando={salvando} podeSalvar={!!nome.trim() && !!sigla.trim()} />}>
      <Field label="Nome"><Input autoFocus value={nome} onChange={(e) => setNome(e.target.value)} placeholder="ex.: Campus Canoas" /></Field>
      <Field label="Sigla"><Input value={sigla} onChange={(e) => setSigla(e.target.value)} placeholder="ex.: CAN" /></Field>
      {erro && <div className="create-err">{erro}</div>}
    </Modal>
  );
}

function CentroModal({ empresa, unidades, editing, onClose, onDone }: {
  empresa: Empresa; unidades: Unidade[]; editing?: CentroDeCusto; onClose: () => void; onDone: () => Promise<void>;
}) {
  const [codigo, setCodigo] = useState(editing?.codigo ?? '');
  const [nome, setNome] = useState(editing?.nome ?? '');
  const [orc, setOrc] = useState(editing ? String(editing.orcamentoAnual) : '');
  const [limite, setLimite] = useState(editing ? String(editing.limiteAutoAprovacao) : '');
  const [sel, setSel] = useState<Set<string>>(new Set(editing?.unidadeIds ?? []));
  const todas = unidades.length > 0 && sel.size === unidades.length;
  const toggle = (id: string) => setSel((s) => { const n = new Set(s); n.has(id) ? n.delete(id) : n.add(id); return n; });
  const { salvando, erro, run } = useSave(async () => {
    const o = Number(orc) || 0, l = Number(limite) || 0;
    editing
      ? await api.updateCentro(editing.id, codigo.trim(), nome.trim(), o, l, [...sel])
      : await api.createCentro(empresa.id, codigo.trim(), nome.trim(), o, l, [...sel]);
    await onDone();
  });
  return (
    <Modal title={editing ? 'Editar centro de custo' : 'Novo centro de custo'} sub={empresa.razaoSocial} onClose={onClose}
      footer={<ModalFooter onClose={onClose} onSalvar={run} salvando={salvando} podeSalvar={!!codigo.trim() && !!nome.trim()} />}>
      <Field label="Código"><Input autoFocus value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="ex.: MKT" /></Field>
      <Field label="Nome"><Input value={nome} onChange={(e) => setNome(e.target.value)} placeholder="ex.: Marketing" /></Field>
      <Field label="Orçamento anual (R$)"><Input value={orc} inputMode="numeric" onChange={(e) => setOrc(e.target.value)} placeholder="ex.: 500000" /></Field>
      <Field label="Limite de auto-aprovação (R$)" help="Despesas até este valor são aprovadas automaticamente. 0 = sem auto-aprovação.">
        <Input value={limite} inputMode="numeric" onChange={(e) => setLimite(e.target.value)} placeholder="ex.: 5000" />
      </Field>
      <Field label="Atua nas unidades">
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
      </Field>
      {erro && <div className="create-err">{erro}</div>}
    </Modal>
  );
}
