import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Plus, ChevronLeft, ClipboardCheck, Check, X, Wallet } from 'lucide-react';
import { api, type Unidade, type CentroDeCusto, type Lancamento, type TipoLancamento, type StatusLancamento } from '../api';
import { useApp } from '../store';
import { brl } from '../lib';
import { Button, Input, Select, Field, PageHeader, Panel, EmptyState, useSave } from '../ui';
import { EmpresaPicker } from './shared';

const STATUS: Record<StatusLancamento, { cls: string; label: string }> = {
  Pendente: { cls: 'badge-pendente', label: 'Pendente' },
  Aprovado: { cls: 'badge-aprovado', label: 'Aprovado' },
  Pago: { cls: 'badge-pago', label: 'Pago' },
  Rejeitado: { cls: 'badge-rejeitado', label: 'Rejeitado' },
};
export const StatusBadge = ({ s }: { s: StatusLancamento }) => <span className={`badge-status ${STATUS[s].cls}`}>{STATUS[s].label}</span>;
const fmtData = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');

/** Mapas id→nome de unidades/centros da empresa (para exibir nomes nas listas). */
function useNomes(empresaId: string) {
  const [unidades, setUnidades] = useState<Unidade[]>([]);
  const [centros, setCentros] = useState<CentroDeCusto[]>([]);
  useEffect(() => {
    if (!empresaId) { setUnidades([]); setCentros([]); return; }
    Promise.all([api.listUnidades(empresaId), api.listCentros(empresaId)]).then(([u, c]) => { setUnidades(u); setCentros(c); }).catch(() => {});
  }, [empresaId]);
  return {
    unidades, centros,
    unidadeNome: useMemo(() => Object.fromEntries(unidades.map((u) => [u.id, u.sigla])), [unidades]),
    centroNome: useMemo(() => Object.fromEntries(centros.map((c) => [c.id, c.nome])), [centros]),
  };
}

/* ============ LISTA (Contas a pagar / receber) ============ */
export function LancamentosPage({ tipo }: { tipo: TipoLancamento }) {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { unidadeNome, centroNome } = useNomes(empresaId);
  const [itens, setItens] = useState<Lancamento[]>([]);

  useEffect(() => {
    if (!empresaId) { setItens([]); return; }
    api.listLancamentos({ empresaId, tipo }).then(setItens).catch(() => {});
  }, [empresaId, tipo]);

  const titulo = tipo === 'Pagar' ? 'Contas a pagar' : 'Contas a receber';

  return (
    <>
      <PageHeader title={titulo} sub={tipo === 'Pagar' ? 'Despesas classificadas por centro de custo e unidade.' : 'Receitas previstas e recebidas.'}>
        <EmpresaPicker />
        <Button variant="brand" icon={<Plus />} disabled={!empresa} onClick={() => nav(`/lancamentos/novo?tipo=${tipo}`)}>Novo lançamento</Button>
      </PageHeader>

      {!empresa ? <EmptyState icon={<Wallet />} title="Nenhuma empresa" sub="Cadastre uma empresa em Estrutura." />
        : itens.length === 0 ? <EmptyState icon={<Wallet />} title={`Nenhuma conta a ${tipo === 'Pagar' ? 'pagar' : 'receber'}`} sub="Crie o primeiro lançamento." />
        : (
          <Panel title={`${itens.length} lançamento(s)`}>
            <table>
              <thead><tr><th>Vencimento</th><th>Descrição</th><th>Contraparte</th><th>Centro de custo</th><th>Unidade</th><th style={{ textAlign: 'right' }}>Valor</th><th>Status</th></tr></thead>
              <tbody>
                {itens.map((l) => (
                  <tr key={l.id} className="row-click" onClick={() => nav(`/lancamentos/${l.id}`)}>
                    <td className="t-mono">{fmtData(l.vencimento)}</td>
                    <td>{l.descricao}</td>
                    <td>{l.contraparte || '—'}</td>
                    <td>{centroNome[l.centroDeCustoId] ?? '—'}</td>
                    <td className="t-mono">{unidadeNome[l.unidadeId] ?? '—'}</td>
                    <td className="amount-cell" style={{ textAlign: 'right' }}>{brl(l.valor)}</td>
                    <td><StatusBadge s={l.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Panel>
        )}
    </>
  );
}

/* ============ APROVAÇÕES ============ */
export function AprovacoesPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { unidadeNome, centroNome } = useNomes(empresaId);
  const [itens, setItens] = useState<Lancamento[]>([]);
  const load = () => { if (empresaId) api.listLancamentos({ empresaId, status: 'Pendente' }).then(setItens).catch(() => {}); };
  useEffect(load, [empresaId]);

  const acao = async (fn: () => Promise<unknown>) => { await fn(); load(); };

  return (
    <>
      <PageHeader title="Aprovações" sub="Lançamentos pendentes aguardando aprovação."><EmpresaPicker /></PageHeader>
      {!empresa ? <EmptyState icon={<ClipboardCheck />} title="Nenhuma empresa" sub="Cadastre uma empresa em Estrutura." />
        : itens.length === 0 ? <EmptyState icon={<ClipboardCheck />} title="Tudo em dia" sub="Não há lançamentos pendentes de aprovação." />
        : (
          <Panel title={`${itens.length} pendente(s)`}>
            <table>
              <thead><tr><th>Vencimento</th><th>Descrição</th><th>Centro de custo</th><th style={{ textAlign: 'right' }}>Valor</th><th className="td-action"></th></tr></thead>
              <tbody>
                {itens.map((l) => (
                  <tr key={l.id}>
                    <td className="t-mono">{fmtData(l.vencimento)}</td>
                    <td className="row-click" onClick={() => nav(`/lancamentos/${l.id}`)}>{l.descricao} <span style={{ color: 'var(--muted-foreground)' }}>· {l.contraparte}</span></td>
                    <td>{centroNome[l.centroDeCustoId] ?? '—'} <span style={{ color: 'var(--muted-foreground)' }}>({unidadeNome[l.unidadeId] ?? '—'})</span></td>
                    <td className="amount-cell" style={{ textAlign: 'right' }}>{brl(l.valor)}</td>
                    <td className="td-action">
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <Button className="btn-approve" icon={<Check />} onClick={() => acao(() => api.aprovarLancamento(l.id))}>Aprovar</Button>
                        <Button icon={<X />} onClick={() => acao(() => api.rejeitarLancamento(l.id, 'Rejeitado na triagem'))}>Rejeitar</Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Panel>
        )}
    </>
  );
}

/* ============ NOVO ============ */
export function NovoLancamentoPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const [sp] = useSearchParams();
  const { unidades, centros } = useNomes(empresaId);

  const [tipo, setTipo] = useState<TipoLancamento>((sp.get('tipo') as TipoLancamento) || 'Pagar');
  const [unidadeId, setUnidadeId] = useState('');
  const [centroId, setCentroId] = useState('');
  const [descricao, setDescricao] = useState('');
  const [contraparte, setContraparte] = useState('');
  const [valor, setValor] = useState('');
  const [vencimento, setVencimento] = useState('');

  const { salvando, erro, run } = useSave(async () => {
    const l = await api.createLancamento({
      empresaId: empresaId!, unidadeId, centroDeCustoId: centroId, tipo,
      descricao: descricao.trim(), contraparte: contraparte.trim(), valor: Number(valor) || 0, vencimento,
    });
    nav(`/lancamentos/${l.id}`);
  });

  const valido = !!unidadeId && !!centroId && !!descricao.trim() && Number(valor) > 0 && !!vencimento;

  return (
    <>
      <button className="backlink" onClick={() => nav(tipo === 'Pagar' ? '/contas-pagar' : '/contas-receber')}><ChevronLeft /> Voltar</button>
      <div className="detail-head"><div><h1 className="detail-title">Novo lançamento</h1>
        <div className="detail-sub">{empresa?.razaoSocial ?? 'Selecione uma empresa'}</div></div></div>

      {!empresa ? <EmptyState icon={<Wallet />} title="Nenhuma empresa" sub="Cadastre uma empresa primeiro." />
        : (
          <div className="card-form">
            <div className="edit-grid">
              <Field label="Tipo">
                <Select value={tipo} onChange={(e) => setTipo(e.target.value as TipoLancamento)}>
                  <option value="Pagar">Conta a pagar</option>
                  <option value="Receber">Conta a receber</option>
                </Select>
              </Field>
              <Field label="Vencimento"><Input type="date" value={vencimento} onChange={(e) => setVencimento(e.target.value)} /></Field>
              <Field label="Unidade">
                <Select value={unidadeId} onChange={(e) => setUnidadeId(e.target.value)}>
                  <option value="">— selecione —</option>
                  {unidades.map((u) => <option key={u.id} value={u.id}>{u.nome} ({u.sigla})</option>)}
                </Select>
              </Field>
              <Field label="Centro de custo">
                <Select value={centroId} onChange={(e) => setCentroId(e.target.value)}>
                  <option value="">— selecione —</option>
                  {centros.map((c) => <option key={c.id} value={c.id}>{c.codigo} · {c.nome}</option>)}
                </Select>
              </Field>
              <Field label="Valor (R$)"><Input value={valor} inputMode="numeric" onChange={(e) => setValor(e.target.value)} placeholder="ex.: 3000" /></Field>
              <Field label={tipo === 'Pagar' ? 'Fornecedor' : 'Cliente'}><Input value={contraparte} onChange={(e) => setContraparte(e.target.value)} placeholder="ex.: Google" /></Field>
            </div>
            <div style={{ marginTop: 14 }}><Field label="Descrição"><Input value={descricao} onChange={(e) => setDescricao(e.target.value)} placeholder="ex.: Campanha de captação" /></Field></div>
            {erro && <div className="create-err" style={{ marginTop: 10 }}>{erro}</div>}
            <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
              <Button variant="brand" disabled={!valido || salvando} onClick={run}>{salvando ? 'Salvando…' : 'Lançar'}</Button>
              <Button onClick={() => nav(-1)}>Cancelar</Button>
            </div>
          </div>
        )}
    </>
  );
}

/* ============ DETALHE ============ */
export function LancamentoDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const [l, setL] = useState<Lancamento | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [nomes, setNomes] = useState<{ centro: string; unidade: string }>({ centro: '—', unidade: '—' });

  async function carregar() {
    if (!id) return;
    setCarregando(true);
    try {
      const lan = await api.getLancamento(id);
      setL(lan);
      const [us, cs] = await Promise.all([api.listUnidades(lan.empresaId), api.listCentros(lan.empresaId)]);
      setNomes({
        centro: cs.find((c) => c.id === lan.centroDeCustoId)?.nome ?? '—',
        unidade: us.find((u) => u.id === lan.unidadeId)?.nome ?? '—',
      });
    } catch { setL(null); } finally { setCarregando(false); }
  }
  useEffect(() => { carregar(); }, [id]);

  if (carregando) return <div className="empty-state">Carregando…</div>;
  if (!l) return <EmptyState icon={<Wallet />} title="Lançamento não encontrado" sub="Ele pode ter sido removido." />;

  const acao = async (fn: () => Promise<Lancamento>) => { const x = await fn(); setL(x); };

  return (
    <>
      <button className="backlink" onClick={() => nav(l.tipo === 'Pagar' ? '/contas-pagar' : '/contas-receber')}><ChevronLeft /> {l.tipo === 'Pagar' ? 'Contas a pagar' : 'Contas a receber'}</button>
      <div className="detail-head">
        <div>
          <h1 className="detail-title">{l.descricao}</h1>
          <div className="detail-sub">{l.tipo === 'Pagar' ? 'Conta a pagar' : 'Conta a receber'} · {l.contraparte || '—'}</div>
        </div>
        <div className="detail-actions">
          {l.status === 'Pendente' && <Button className="btn-approve" icon={<Check />} onClick={() => acao(() => api.aprovarLancamento(l.id))}>Aprovar</Button>}
          {l.status === 'Pendente' && <Button icon={<X />} onClick={() => acao(() => api.rejeitarLancamento(l.id, 'Rejeitado'))}>Rejeitar</Button>}
          {l.status === 'Aprovado' && <Button variant="brand" onClick={() => acao(() => api.pagarLancamento(l.id))}>Marcar como pago</Button>}
        </div>
      </div>

      <div className="info-grid">
        <div className="info-card"><div className="info-label">Valor</div><div className="info-value">{brl(l.valor)}</div></div>
        <div className="info-card"><div className="info-label">Vencimento</div><div className="info-value">{fmtData(l.vencimento)}</div></div>
        <div className="info-card"><div className="info-label">Status</div><div style={{ marginTop: 4 }}><StatusBadge s={l.status} /></div></div>
        <div className="info-card"><div className="info-label">Centro de custo</div><div className="info-value" style={{ fontSize: 15 }}>{nomes.centro}</div></div>
        <div className="info-card"><div className="info-label">Unidade</div><div className="info-value" style={{ fontSize: 15 }}>{nomes.unidade}</div></div>
      </div>

      {l.motivoRejeicao && <p className="create-err">Motivo da rejeição: {l.motivoRejeicao}</p>}
    </>
  );
}
