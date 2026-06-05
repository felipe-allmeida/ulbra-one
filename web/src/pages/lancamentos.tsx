import { useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Plus, ChevronLeft, ClipboardCheck, Check, X, Wallet } from 'lucide-react';
import { type TransactionType, type TransactionStatus } from '../api';
import { useApp } from '../store';
import {
  useApproveTransaction,
  useCostCenters,
  useCreateTransaction,
  usePayTransaction,
  useRejectTransaction,
  useTransaction,
  useTransactions,
  useUnits,
} from '../queries';
import { brl } from '../lib';
import { getErrorMessage } from '../apiError';
import { Button, Input, Select, Field, PageHeader, Panel, EmptyState } from '../ui';
import { EmpresaPicker } from './shared';

const STATUS: Record<TransactionStatus, { cls: string; label: string }> = {
  Pending: { cls: 'badge-pendente', label: 'Pendente' },
  Approved: { cls: 'badge-aprovado', label: 'Aprovado' },
  Paid: { cls: 'badge-pago', label: 'Pago' },
  Rejected: { cls: 'badge-rejeitado', label: 'Rejeitado' },
};
export const StatusBadge = ({ s }: { s: TransactionStatus }) => (
  <span className={`badge-status ${STATUS[s].cls}`}>{STATUS[s].label}</span>
);
const fmtData = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');

/** Mapas id→nome de unidades/centros da empresa (para exibir nomes nas listas). */
function useNomes(empresaId: string) {
  const unidades = useUnits(empresaId).data ?? [];
  const centros = useCostCenters(empresaId).data ?? [];
  return {
    unidades,
    centros,
    unidadeNome: useMemo(() => Object.fromEntries(unidades.map((u) => [u.id, u.acronym])), [unidades]),
    centroNome: useMemo(() => Object.fromEntries(centros.map((c) => [c.id, c.name])), [centros]),
  };
}

/* ============ LISTA (Contas a pagar / receber) ============ */
export function LancamentosPage({ tipo }: { tipo: TransactionType }) {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { unidadeNome, centroNome } = useNomes(empresaId);
  const itens = useTransactions({ companyId: empresaId, type: tipo }, { enabled: !!empresaId }).data ?? [];

  const titulo = tipo === 'Payable' ? 'Contas a pagar' : 'Contas a receber';

  return (
    <>
      <PageHeader
        title={titulo}
        sub={
          tipo === 'Payable'
            ? 'Despesas classificadas por centro de custo e unidade.'
            : 'Receitas previstas e recebidas.'
        }
      >
        <EmpresaPicker />
        <Button
          variant='brand'
          icon={<Plus />}
          disabled={!empresa}
          onClick={() => nav(`/app/lancamentos/novo?tipo=${tipo}`)}
        >
          Novo lançamento
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState icon={<Wallet />} title='Nenhuma empresa' sub='Cadastre uma empresa em Estrutura.' />
      ) : itens.length === 0 ? (
        <EmptyState
          icon={<Wallet />}
          title={`Nenhuma conta a ${tipo === 'Payable' ? 'pagar' : 'receber'}`}
          sub='Crie o primeiro lançamento.'
        />
      ) : (
        <Panel title={`${itens.length} lançamento(s)`}>
          <table>
            <thead>
              <tr>
                <th>Vencimento</th>
                <th>Descrição</th>
                <th>Contraparte</th>
                <th>Centro de custo</th>
                <th>Unidade</th>
                <th style={{ textAlign: 'right' }}>Valor</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {itens.map((l) => (
                <tr key={l.id} className='row-click' onClick={() => nav(`/app/lancamentos/${l.id}`)}>
                  <td className='t-mono'>{fmtData(l.dueDate)}</td>
                  <td>{l.description}</td>
                  <td>{l.counterparty || '—'}</td>
                  <td>{centroNome[l.costCenterId] ?? '—'}</td>
                  <td className='t-mono'>{unidadeNome[l.unitId] ?? '—'}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(l.amount)}
                  </td>
                  <td>
                    <StatusBadge s={l.status} />
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

/* ============ APROVAÇÕES ============ */
export function AprovacoesPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { unidadeNome, centroNome } = useNomes(empresaId);
  const itens = useTransactions({ companyId: empresaId, status: 'Pending' }, { enabled: !!empresaId }).data ?? [];
  const approve = useApproveTransaction();
  const reject = useRejectTransaction();

  return (
    <>
      <PageHeader title='Aprovações' sub='Lançamentos pendentes aguardando aprovação.'>
        <EmpresaPicker />
      </PageHeader>
      {!empresa ? (
        <EmptyState icon={<ClipboardCheck />} title='Nenhuma empresa' sub='Cadastre uma empresa em Estrutura.' />
      ) : itens.length === 0 ? (
        <EmptyState icon={<ClipboardCheck />} title='Tudo em dia' sub='Não há lançamentos pendentes de aprovação.' />
      ) : (
        <Panel title={`${itens.length} pendente(s)`}>
          <table>
            <thead>
              <tr>
                <th>Vencimento</th>
                <th>Descrição</th>
                <th>Centro de custo</th>
                <th style={{ textAlign: 'right' }}>Valor</th>
                <th className='td-action'></th>
              </tr>
            </thead>
            <tbody>
              {itens.map((l) => (
                <tr key={l.id}>
                  <td className='t-mono'>{fmtData(l.dueDate)}</td>
                  <td className='row-click' onClick={() => nav(`/app/lancamentos/${l.id}`)}>
                    {l.description} <span style={{ color: 'var(--muted-foreground)' }}>· {l.counterparty}</span>
                  </td>
                  <td>
                    {centroNome[l.costCenterId] ?? '—'}{' '}
                    <span style={{ color: 'var(--muted-foreground)' }}>({unidadeNome[l.unitId] ?? '—'})</span>
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(l.amount)}
                  </td>
                  <td className='td-action'>
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      <Button className='btn-approve' icon={<Check />} onClick={() => approve.mutate({ id: l.id })}>
                        Aprovar
                      </Button>
                      <Button icon={<X />} onClick={() => reject.mutate({ id: l.id, reason: 'Rejeitado na triagem' })}>
                        Rejeitar
                      </Button>
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

  const [tipo, setTipo] = useState<TransactionType>((sp.get('tipo') as TransactionType) || 'Payable');
  const [unidadeId, setUnidadeId] = useState('');
  const [centroId, setCentroId] = useState('');
  const [descricao, setDescricao] = useState('');
  const [contraparte, setContraparte] = useState('');
  const [valor, setValor] = useState('');
  const [vencimento, setVencimento] = useState('');

  const create = useCreateTransaction({ onSuccess: (l) => nav(`/app/lancamentos/${l.id}`) });
  const salvando = create.isPending;
  const erro = create.error ? getErrorMessage(create.error) : null;
  const run = () =>
    create.mutate({
      companyId: empresaId,
      unitId: unidadeId,
      costCenterId: centroId,
      type: tipo,
      description: descricao.trim(),
      counterparty: contraparte.trim(),
      amount: Number(valor) || 0,
      dueDate: vencimento,
    });

  const valido = !!unidadeId && !!centroId && !!descricao.trim() && Number(valor) > 0 && !!vencimento;

  return (
    <>
      <button
        className='backlink'
        onClick={() => nav(tipo === 'Payable' ? '/app/contas-pagar' : '/app/contas-receber')}
      >
        <ChevronLeft /> Voltar
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>Novo lançamento</h1>
          <div className='detail-sub'>{empresa?.legalName ?? 'Selecione uma empresa'}</div>
        </div>
      </div>

      {!empresa ? (
        <EmptyState icon={<Wallet />} title='Nenhuma empresa' sub='Cadastre uma empresa primeiro.' />
      ) : (
        <div className='card-form'>
          <div className='edit-grid'>
            <Field label='Tipo'>
              <Select value={tipo} onChange={(e) => setTipo(e.target.value as TransactionType)}>
                <option value='Payable'>Conta a pagar</option>
                <option value='Receivable'>Conta a receber</option>
              </Select>
            </Field>
            <Field label='Vencimento'>
              <Input type='date' value={vencimento} onChange={(e) => setVencimento(e.target.value)} />
            </Field>
            <Field label='Unidade'>
              <Select value={unidadeId} onChange={(e) => setUnidadeId(e.target.value)}>
                <option value=''>— selecione —</option>
                {unidades.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.name} ({u.acronym})
                  </option>
                ))}
              </Select>
            </Field>
            <Field label='Centro de custo'>
              <Select value={centroId} onChange={(e) => setCentroId(e.target.value)}>
                <option value=''>— selecione —</option>
                {centros.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.code} · {c.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label='Valor (R$)'>
              <Input
                value={valor}
                inputMode='numeric'
                onChange={(e) => setValor(e.target.value)}
                placeholder='ex.: 3000'
              />
            </Field>
            <Field label={tipo === 'Payable' ? 'Fornecedor' : 'Cliente'}>
              <Input value={contraparte} onChange={(e) => setContraparte(e.target.value)} placeholder='ex.: Google' />
            </Field>
          </div>
          <div style={{ marginTop: 14 }}>
            <Field label='Descrição'>
              <Input
                value={descricao}
                onChange={(e) => setDescricao(e.target.value)}
                placeholder='ex.: Campanha de captação'
              />
            </Field>
          </div>
          {erro && (
            <div className='create-err' style={{ marginTop: 10 }}>
              {erro}
            </div>
          )}
          <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
            <Button variant='brand' disabled={!valido || salvando} onClick={run}>
              {salvando ? 'Salvando…' : 'Lançar'}
            </Button>
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
  const query = useTransaction(id);
  const l = query.data ?? null;
  const carregando = query.isLoading;

  const units = useUnits(l?.companyId ?? '').data ?? [];
  const centros = useCostCenters(l?.companyId ?? '').data ?? [];
  const nomes = {
    centro: centros.find((c) => c.id === l?.costCenterId)?.name ?? '—',
    unidade: units.find((u) => u.id === l?.unitId)?.name ?? '—',
  };

  const approve = useApproveTransaction();
  const reject = useRejectTransaction();
  const pay = usePayTransaction();

  if (carregando) return <div className='empty-state'>Carregando…</div>;
  if (!l) return <EmptyState icon={<Wallet />} title='Lançamento não encontrado' sub='Ele pode ter sido removido.' />;

  return (
    <>
      <button
        className='backlink'
        onClick={() => nav(l.type === 'Payable' ? '/app/contas-pagar' : '/app/contas-receber')}
      >
        <ChevronLeft /> {l.type === 'Payable' ? 'Contas a pagar' : 'Contas a receber'}
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{l.description}</h1>
          <div className='detail-sub'>
            {l.type === 'Payable' ? 'Conta a pagar' : 'Conta a receber'} · {l.counterparty || '—'}
          </div>
        </div>
        <div className='detail-actions'>
          {l.status === 'Pending' && (
            <Button className='btn-approve' icon={<Check />} onClick={() => approve.mutate({ id: l.id })}>
              Aprovar
            </Button>
          )}
          {l.status === 'Pending' && (
            <Button icon={<X />} onClick={() => reject.mutate({ id: l.id, reason: 'Rejeitado' })}>
              Rejeitar
            </Button>
          )}
          {l.status === 'Approved' && (
            <Button variant='brand' onClick={() => pay.mutate({ id: l.id })}>
              Marcar como pago
            </Button>
          )}
        </div>
      </div>

      <div className='info-grid'>
        <div className='info-card'>
          <div className='info-label'>Valor</div>
          <div className='info-value'>{brl(l.amount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>Vencimento</div>
          <div className='info-value'>{fmtData(l.dueDate)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>Status</div>
          <div style={{ marginTop: 4 }}>
            <StatusBadge s={l.status} />
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>Centro de custo</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {nomes.centro}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>Unidade</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {nomes.unidade}
          </div>
        </div>
      </div>

      {l.rejectionReason && <p className='create-err'>Motivo da rejeição: {l.rejectionReason}</p>}
    </>
  );
}
