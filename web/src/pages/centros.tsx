import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Plus, Target, ChevronLeft, Pencil, Activity, Zap } from 'lucide-react';
import { useApp } from '../store';
import { useCostCenter, useCostCenters, useTransactions, useUnits } from '../queries';
import { brlCompact, brl } from '../lib';
import { Button, Chip, Kpi, EmptyState, PageHeader, Panel } from '../ui';
import { EmpresaPicker, CentroForm } from './shared';
import { StatusBadge } from './lancamentos';
import { fmtDate } from '../datetime';

/* ===================== LIST ===================== */
export function CentrosListPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const unidades = useUnits(empresaId).data ?? [];
  const centros = useCostCenters(empresaId).data ?? [];

  const unidadeSigla = useMemo(() => Object.fromEntries(unidades.map((u) => [u.id, u.acronym])), [unidades]);
  const orcamentoTotal = centros.reduce((s, c) => s + c.annualBudget, 0);

  return (
    <>
      <PageHeader
        title='Centros de custo'
        sub='Áreas funcionais da empresa. Um centro de custo (ex.: Marketing) pode atuar em várias unidades.'
      >
        <EmpresaPicker />
        <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => nav('/app/accounting/cost-centers/new')}>
          Novo centro de custo
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Target />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : (
        <>
          <div className='kpi-grid'>
            <Kpi label='Centros de custo' value={String(centros.length)} />
            <Kpi
              label='Orçamento total (anual)'
              value={brlCompact(orcamentoTotal)}
              foot={`${centros.length} centro(s)`}
            />
            <Kpi label='Unidades' value={String(unidades.length)} foot={empresa.legalName} />
          </div>

          {centros.length === 0 ? (
            <EmptyState
              icon={<Target />}
              title='Nenhum centro de custo'
              sub='Crie o primeiro centro de custo desta empresa.'
            />
          ) : (
            <div className='cc-grid'>
              {centros.map((c) => {
                const pct = orcamentoTotal > 0 ? Math.round((c.annualBudget / orcamentoTotal) * 100) : 0;
                const todas = unidades.length > 0 && c.unitIds.length >= unidades.length;
                return (
                  <div className='cc-card' key={c.id} onClick={() => nav(`/app/accounting/cost-centers/${c.id}`)} title='Abrir'>
                    <div className='cc-card-top'>
                      <div>
                        <div className='cc-name'>{c.name}</div>
                        <div className='cc-code'>{c.code}</div>
                      </div>
                      <div className='cc-orc'>
                        <div className='v'>{brlCompact(c.annualBudget)}</div>
                        <div className='l'>orçamento/ano</div>
                      </div>
                    </div>
                    <div className='cc-progress'>
                      <div className='cc-progress-bar' style={{ width: `${pct}%` }} />
                    </div>
                    <div className='cc-meta'>
                      <span>participação no orçamento</span>
                      <span>{pct}%</span>
                    </div>
                    <div className='chips'>
                      {c.unitIds.length === 0 && <Chip>sem unidade</Chip>}
                      {todas ? (
                        <Chip tone='brand'>Todas as unidades</Chip>
                      ) : (
                        c.unitIds.map((id) => <Chip key={id}>{unidadeSigla[id] ?? '—'}</Chip>)
                      )}
                      {c.autoApprovalLimit > 0 && (
                        <Chip tone='auto' icon={<Zap />}>
                          auto ≤ {brlCompact(c.autoApprovalLimit)}
                        </Chip>
                      )}
                    </div>
                  </div>
                );
              })}
              <button className='add-card' onClick={() => nav('/app/accounting/cost-centers/new')}>
                <Plus /> Novo centro de custo
              </button>
            </div>
          )}
        </>
      )}
    </>
  );
}

/* ===================== DETAIL / NEW ===================== */
export function CentroDetailPage({ modo }: { modo: 'novo' | 'detalhe' }) {
  const { id } = useParams();
  const nav = useNavigate();
  const { empresa, empresas } = useApp();

  const [editando, setEditando] = useState(modo === 'novo');

  const centroQuery = useCostCenter(modo === 'detalhe' ? id : undefined);
  const centro = modo === 'detalhe' ? (centroQuery.data ?? null) : null;
  const carregando = modo === 'detalhe' && centroQuery.isLoading;

  // The center's company (in detail) or the selected company (in new).
  const empresaDoCentro = centro ? (empresas.find((e) => e.id === centro.companyId) ?? null) : empresa;

  // Units come from the center's company (detail) or the selected company (new).
  const unitsCompanyId = modo === 'novo' ? (empresa?.id ?? '') : (centro?.companyId ?? '');
  const unidades = useUnits(unitsCompanyId).data ?? [];
  // Detail needs all of the center's transactions (activity list + "spent" sum), not just page 1.
  // pageSize is capped at 100 server-side; a server-computed aggregate is the proper long-term fix.
  const lancamentos =
    useTransactions({ costCenterId: centro?.id, pageSize: 100 }, { enabled: !!centro?.id }).data?.items ?? [];

  /* ---- NEW ---- */
  if (modo === 'novo') {
    return (
      <>
        <button className='backlink' onClick={() => nav('/app/accounting/cost-centers')}>
          <ChevronLeft /> Centros de custo
        </button>
        <div className='detail-head'>
          <div>
            <h1 className='detail-title'>Novo centro de custo</h1>
            <div className='detail-sub'>{empresa?.legalName ?? 'Selecione uma empresa'}</div>
          </div>
        </div>
        {empresa ? (
          <div className='card-form'>
            <CentroForm
              empresa={empresa}
              unidades={unidades}
              onSaved={(cc) => nav(`/app/accounting/cost-centers/${cc.id}`)}
              onCancel={() => nav('/app/accounting/cost-centers')}
            />
          </div>
        ) : (
          <EmptyState icon={<Target />} title='Nenhuma empresa' sub='Cadastre uma empresa primeiro.' />
        )}
      </>
    );
  }

  /* ---- DETAIL ---- */
  if (carregando) return <div className='empty-state'>Carregando…</div>;
  if (!centro || !empresaDoCentro)
    return <EmptyState icon={<Target />} title='Centro de custo não encontrado' sub='Ele pode ter sido removido.' />;

  const todas = unidades.length > 0 && centro.unitIds.length >= unidades.length;
  const cobertura = todas
    ? 'Todas as unidades'
    : centro.unitIds.length === 0
      ? 'Nenhuma unidade'
      : centro.unitIds.map((id) => unidades.find((u) => u.id === id)?.acronym ?? '—').join(', ');

  const executado = lancamentos
    .filter((l) => l.type === 'Payable' && (l.status === 'Approved' || l.status === 'Paid'))
    .reduce((s, l) => s + l.amount, 0);
  const pctExec = centro.annualBudget > 0 ? Math.round((executado / centro.annualBudget) * 100) : 0;

  return (
    <>
      <button className='backlink' onClick={() => nav('/app/accounting/cost-centers')}>
        <ChevronLeft /> Centros de custo
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>
            <Target size={20} /> {centro.name}
          </h1>
          <div className='detail-sub'>
            {centro.code} · {empresaDoCentro.legalName}
          </div>
        </div>
        {!editando && (
          <div className='detail-actions'>
            <Button icon={<Pencil />} onClick={() => setEditando(true)}>
              Editar
            </Button>
          </div>
        )}
      </div>

      {editando ? (
        <div className='card-form'>
          <CentroForm
            empresa={empresaDoCentro}
            unidades={unidades}
            initial={centro}
            onSaved={() => setEditando(false)}
            onCancel={() => setEditando(false)}
          />
        </div>
      ) : (
        <>
          <div className='info-grid'>
            <Info label='Orçamento anual' value={brl(centro.annualBudget)} />
            <Info label='Executado' value={brl(executado)} foot={`${pctExec}% do orçamento`} />
            <Info
              label='Auto-aprovação'
              value={centro.autoApprovalLimit > 0 ? `até ${brl(centro.autoApprovalLimit)}` : 'desativada'}
            />
            <Info label='Cobertura' value={cobertura} />
          </div>

          <h2 className='section-title'>Atividade</h2>
          {lancamentos.length === 0 ? (
            <div className='activity-empty'>
              <Activity />
              Nenhum lançamento neste centro de custo ainda.
            </div>
          ) : (
            <Panel title={`${lancamentos.length} lançamento(s)`}>
              <table>
                <thead>
                  <tr>
                    <th>Vencimento</th>
                    <th>Descrição</th>
                    <th>Tipo</th>
                    <th style={{ textAlign: 'right' }}>Valor</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {lancamentos.map((l) => (
                    <tr key={l.id} className='row-click' onClick={() => nav(`/app/entries/${l.id}`)}>
                      <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                      <td>{l.description}</td>
                      <td>{l.type === 'Payable' ? 'A pagar' : 'A receber'}</td>
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
      )}
    </>
  );
}

function Info({ label, value, foot }: { label: string; value: string; foot?: string }) {
  return (
    <div className='info-card'>
      <div className='info-label'>{label}</div>
      <div className='info-value'>{value}</div>
      {foot && <div className='kpi-foot'>{foot}</div>}
    </div>
  );
}
