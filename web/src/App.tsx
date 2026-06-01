import { useEffect, useState, type ReactNode } from 'react';
import { Building2, LogIn, LogOut, Plus, Network, Wallet } from 'lucide-react';
import { api, auth, type Empresa, type Unidade, type CentroDeCusto, type Usuario } from './api';

function moeda(v: number) {
  return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

export default function App() {
  const [user, setUser] = useState<Usuario | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  const [empresas, setEmpresas] = useState<Empresa[]>([]);
  const [empresaSel, setEmpresaSel] = useState<Empresa | null>(null);
  const [unidades, setUnidades] = useState<Unidade[]>([]);
  const [unidadeSel, setUnidadeSel] = useState<Unidade | null>(null);
  const [centros, setCentros] = useState<CentroDeCusto[]>([]);

  async function carregarEmpresas() {
    setEmpresas(await api.listEmpresas());
  }

  useEffect(() => {
    if (!auth.token) return;
    api.me()
      .then((u) => { setUser(u); return carregarEmpresas(); })
      .catch(() => auth.clear());
  }, []);

  async function login() {
    setErro(null);
    try {
      const { token, usuario } = await api.devLogin('felipe.allmeida.dev@gmail.com', 'Felipe de Almeida');
      auth.set(token);
      setUser(usuario);
      await carregarEmpresas();
    } catch (e) {
      setErro(String(e));
    }
  }

  function logout() {
    auth.clear();
    setUser(null);
    setEmpresas([]); setEmpresaSel(null); setUnidades([]); setUnidadeSel(null); setCentros([]);
  }

  async function selecionarEmpresa(e: Empresa) {
    setEmpresaSel(e); setUnidadeSel(null); setCentros([]);
    setUnidades(await api.listUnidades(e.id));
  }

  async function selecionarUnidade(u: Unidade) {
    setUnidadeSel(u);
    setCentros(await api.listCentros(u.id));
  }

  if (!user) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-50">
        <div className="bg-white rounded-2xl shadow-sm border border-slate-200 p-10 w-[380px] text-center">
          <div className="mx-auto w-12 h-12 rounded-xl bg-slate-900 text-white grid place-items-center mb-4">
            <Network size={24} />
          </div>
          <h1 className="text-xl font-semibold text-slate-900">ULBRA One</h1>
          <p className="text-sm text-slate-500 mt-1 mb-6">ERP — Organograma</p>
          <button
            onClick={login}
            className="w-full flex items-center justify-center gap-2 bg-slate-900 text-white rounded-lg py-2.5 text-sm font-medium hover:bg-slate-800 transition"
          >
            <LogIn size={16} /> Entrar (dev)
          </button>
          {erro && <p className="text-xs text-red-600 mt-4 break-words">{erro}</p>}
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="bg-white border-b border-slate-200 px-6 py-3 flex items-center justify-between sticky top-0">
        <div className="flex items-center gap-2 font-semibold">
          <span className="w-7 h-7 rounded-lg bg-slate-900 text-white grid place-items-center"><Network size={16} /></span>
          ULBRA One
        </div>
        <div className="flex items-center gap-3 text-sm">
          <span className="text-slate-500">{user.nome} · <span className="font-medium text-slate-700">{user.role}</span></span>
          <button onClick={logout} className="flex items-center gap-1 text-slate-500 hover:text-slate-900">
            <LogOut size={15} /> sair
          </button>
        </div>
      </header>

      <main className="max-w-6xl mx-auto p-6 grid grid-cols-3 gap-4">
        <Coluna titulo="Empresas" icon={<Building2 size={16} />}>
          <CriarItem
            campos={[['razao', 'Razão social'], ['cnpj', 'CNPJ (14 dígitos)']]}
            onCriar={async (v) => { await api.createEmpresa(v.razao, v.cnpj); await carregarEmpresas(); }}
          />
          {empresas.map((e) => (
            <Item key={e.id} ativo={empresaSel?.id === e.id} onClick={() => selecionarEmpresa(e)}
              titulo={e.razaoSocial} sub={e.cnpj} />
          ))}
        </Coluna>

        <Coluna titulo="Unidades" icon={<Network size={16} />} vazio={!empresaSel ? 'Selecione uma empresa' : undefined}>
          {empresaSel && (
            <CriarItem
              campos={[['nome', 'Nome'], ['sigla', 'Sigla']]}
              onCriar={async (v) => { await api.createUnidade(empresaSel.id, v.nome, v.sigla); setUnidades(await api.listUnidades(empresaSel.id)); }}
            />
          )}
          {unidades.map((u) => (
            <Item key={u.id} ativo={unidadeSel?.id === u.id} onClick={() => selecionarUnidade(u)}
              titulo={u.nome} sub={u.sigla} />
          ))}
        </Coluna>

        <Coluna titulo="Centros de Custo" icon={<Wallet size={16} />} vazio={!unidadeSel ? 'Selecione uma unidade' : undefined}>
          {unidadeSel && (
            <CriarItem
              campos={[['codigo', 'Código'], ['nome', 'Nome'], ['orcamento', 'Orçamento anual']]}
              onCriar={async (v) => { await api.createCentro(unidadeSel.id, v.codigo, v.nome, Number(v.orcamento) || 0); setCentros(await api.listCentros(unidadeSel.id)); }}
            />
          )}
          {centros.map((c) => (
            <Item key={c.id} titulo={`${c.codigo} · ${c.nome}`} sub={moeda(c.orcamentoAnual)} />
          ))}
        </Coluna>
      </main>
    </div>
  );
}

function Coluna({ titulo, icon, vazio, children }: { titulo: string; icon: ReactNode; vazio?: string; children: ReactNode }) {
  return (
    <section className="bg-white rounded-xl border border-slate-200 p-4">
      <h2 className="flex items-center gap-2 text-sm font-semibold text-slate-700 mb-3">{icon} {titulo}</h2>
      {vazio ? <p className="text-xs text-slate-400 py-6 text-center">{vazio}</p> : <div className="space-y-1.5">{children}</div>}
    </section>
  );
}

function Item({ titulo, sub, ativo, onClick }: { titulo: string; sub?: string; ativo?: boolean; onClick?: () => void }) {
  return (
    <button
      onClick={onClick}
      className={`w-full text-left px-3 py-2 rounded-lg border text-sm transition ${
        ativo ? 'border-slate-900 bg-slate-50' : 'border-slate-100 hover:border-slate-300'
      } ${onClick ? 'cursor-pointer' : 'cursor-default'}`}
    >
      <div className="font-medium text-slate-800">{titulo}</div>
      {sub && <div className="text-xs text-slate-500">{sub}</div>}
    </button>
  );
}

function CriarItem({ campos, onCriar }: { campos: [string, string][]; onCriar: (v: Record<string, string>) => Promise<void> }) {
  const [aberto, setAberto] = useState(false);
  const [valores, setValores] = useState<Record<string, string>>({});
  const [erro, setErro] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  if (!aberto) {
    return (
      <button onClick={() => setAberto(true)} className="w-full flex items-center justify-center gap-1 text-xs text-slate-500 border border-dashed border-slate-300 rounded-lg py-2 hover:border-slate-400 hover:text-slate-700">
        <Plus size={14} /> novo
      </button>
    );
  }

  return (
    <div className="border border-slate-200 rounded-lg p-2.5 space-y-2 bg-slate-50">
      {campos.map(([k, label]) => (
        <input
          key={k}
          placeholder={label}
          value={valores[k] ?? ''}
          onChange={(ev) => setValores((s) => ({ ...s, [k]: ev.target.value }))}
          className="w-full text-sm border border-slate-200 rounded px-2 py-1.5 bg-white outline-none focus:border-slate-400"
        />
      ))}
      {erro && <p className="text-xs text-red-600 break-words">{erro}</p>}
      <div className="flex gap-2">
        <button
          disabled={salvando}
          onClick={async () => {
            setErro(null); setSalvando(true);
            try { await onCriar(valores); setValores({}); setAberto(false); }
            catch (e) { setErro(String(e)); }
            finally { setSalvando(false); }
          }}
          className="flex-1 bg-slate-900 text-white rounded py-1.5 text-xs font-medium hover:bg-slate-800 disabled:opacity-50"
        >salvar</button>
        <button onClick={() => { setAberto(false); setErro(null); }} className="px-3 text-xs text-slate-500 hover:text-slate-800">cancelar</button>
      </div>
    </div>
  );
}
