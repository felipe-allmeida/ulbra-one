import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Plus, Target, Pencil, Zap } from 'lucide-react';
import { useApp } from '../store';
import { useCostCenter, useCostCenters, useCostCenterRealized, useUnits } from '../queries';
import { brl } from '../lib';
import { Button, Chip, cx, Kpi, EmptyState, PageHeader, LoadingState, Skeleton } from '../ui';
import { P, usePermissions } from '../permissions';
import { BackLink, useOriginNav } from '../nav';
import { CentroForm } from './shared';
import { CostCenterBudgetPanel } from './centros-budget';
import { CostCenterActivity } from './centros-activity';

/* ===================== LIST ===================== */
export function CentrosListPage() {
  const { empresa, empresaId, empresas, isAllCompanies, scopeReady } = useApp();
  const nav = useNavigate();
  const openDetail = useOriginNav();
  const perms = usePermissions();
  // Consolidated "Todas" (AEL-906): read-only; hide create; budget-share mode (realized is per-company).
  const canWrite = perms.can(P.coreWrite) && !isAllCompanies;
  // Per-cost-center executado (realized) needs requisitions:read. Every role that reaches this screen
  // has it; the gate is just a safeguard — without it the card falls back to the "budget share" mode.
  const canReadUsage = perms.can(P.requisitionsRead) && !isAllCompanies;
  const unidades = useUnits(empresaId).data ?? [];
  const centrosQuery = useCostCenters(empresaId);
  const centros = centrosQuery.data ?? [];
  const realizadoList = useCostCenterRealized(empresaId, { enabled: canReadUsage }).data ?? [];
  const nomeEmpresa = (id: string) => empresas.find((e) => e.id === id)?.legalName ?? '—';

  const unidadeSigla = useMemo(() => Object.fromEntries(unidades.map((u) => [u.id, u.acronym])), [unidades]);
  const realizadoPorCentro = useMemo(
    () => Object.fromEntries(realizadoList.map((r) => [r.costCenterId, r.realized])),
    [realizadoList],
  );
  const budgetTotal = centros.reduce((s, c) => s + c.annualBudget, 0);

  return (
    <>
      <PageHeader
        title='Naturezas orçamentárias'
        sub='Áreas funcionais da empresa. Uma natureza orçamentária (ex.: Marketing) pode atuar em várias unidades.'
      >
        {canWrite && (
          <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => nav('/app/cost-centers/new')}>
            Nova natureza orçamentária
          </Button>
        )}
      </PageHeader>

      {!scopeReady ? (
        <EmptyState
          icon={<Target />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : (
        <>
          <div className='kpi-grid'>
            <Kpi label='Naturezas orçamentárias' value={String(centros.length)} />
            <Kpi label='Orçamento total (anual)' value={brl(budgetTotal)} foot={`${centros.length} centro(s)`} />
            <Kpi label='Unidades' value={String(unidades.length)} foot={empresa?.legalName ?? 'Todas as empresas'} />
          </div>

          {centrosQuery.isLoading ? (
            <div className='cc-grid'>
              {Array.from({ length: 4 }).map((_, i) => (
                <div className='cc-card' key={i}>
                  <div className='cc-card-top'>
                    <div>
                      <Skeleton width={150} />
                      <div style={{ marginTop: 8 }}>
                        <Skeleton width={90} height={10} />
                      </div>
                    </div>
                    <Skeleton width={70} />
                  </div>
                </div>
              ))}
            </div>
          ) : centros.length === 0 ? (
            <EmptyState
              icon={<Target />}
              title='Nenhuma natureza orçamentária'
              sub='Crie a primeira natureza orçamentária desta empresa.'
            />
          ) : (
            <div className='cc-grid'>
              {centros.map((c) => {
                const sharePct = budgetTotal > 0 ? Math.round((c.annualBudget / budgetTotal) * 100) : 0;
                const executado = realizadoPorCentro[c.id] ?? 0;
                const disponivel = c.annualBudget - executado;
                const usoPct = c.annualBudget > 0 ? Math.round((executado / c.annualBudget) * 100) : 0;
                const barPct = Math.min(canReadUsage ? usoPct : sharePct, 100);
                const over = canReadUsage && usoPct > 100;
                const todas = unidades.length > 0 && c.unitIds.length >= unidades.length;
                return (
                  <div
                    className='cc-card'
                    key={c.id}
                    onClick={() => openDetail(`/app/cost-centers/${c.id}`)}
                    title='Abrir'
                  >
                    <div className='cc-card-top'>
                      <div>
                        <div className='cc-name'>{c.name}</div>
                        <div className='cc-code'>
                          {c.code}
                          {isAllCompanies && ` · ${nomeEmpresa(c.companyId)}`}
                        </div>
                      </div>
                      <div className='cc-orc'>
                        {canReadUsage ? (
                          <>
                            <div className={cx('v', disponivel < 0 && 'bad')}>{brl(disponivel)}</div>
                            <div className='l'>disponível</div>
                            <div className='cc-orc-sub'>orçamento/ano {brl(c.annualBudget)}</div>
                          </>
                        ) : (
                          <>
                            <div className='v'>{brl(c.annualBudget)}</div>
                            <div className='l'>orçamento/ano</div>
                          </>
                        )}
                      </div>
                    </div>
                    <div className='cc-progress'>
                      <div className={cx('cc-progress-bar', over && 'over')} style={{ width: `${barPct}%` }} />
                    </div>
                    <div className='cc-meta'>
                      {canReadUsage ? (
                        <>
                          <span>gasto</span>
                          <span>{brl(executado)}</span>
                        </>
                      ) : (
                        <>
                          <span>participação no orçamento</span>
                          <span>{sharePct}%</span>
                        </>
                      )}
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
                          auto ≤ {brl(c.autoApprovalLimit)}
                        </Chip>
                      )}
                    </div>
                  </div>
                );
              })}
              {canWrite && (
                <button className='add-card' onClick={() => nav('/app/cost-centers/new')}>
                  <Plus /> Nova natureza orçamentária
                </button>
              )}
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
  const canWrite = usePermissions().can(P.coreWrite);

  const [editando, setEditando] = useState(modo === 'novo');

  const centroQuery = useCostCenter(modo === 'detalhe' ? id : undefined);
  const centro = modo === 'detalhe' ? (centroQuery.data ?? null) : null;
  const carregando = modo === 'detalhe' && centroQuery.isLoading;

  // The center's company (in detail) or the selected company (in new).
  const empresaDoCentro = centro ? (empresas.find((e) => e.id === centro.companyId) ?? null) : empresa;

  // Units come from the center's company (detail) or the selected company (new).
  const unitsCompanyId = modo === 'novo' ? (empresa?.id ?? '') : (centro?.companyId ?? '');
  const unidades = useUnits(unitsCompanyId).data ?? [];

  /* ---- NEW ---- */
  if (modo === 'novo') {
    return (
      <>
        <BackLink fallback='/app/cost-centers' />
        <div className='detail-head'>
          <div>
            <h1 className='detail-title'>Nova natureza orçamentária</h1>
            <div className='detail-sub'>{empresa?.legalName ?? 'Selecione uma empresa'}</div>
          </div>
        </div>
        {empresa ? (
          <div className='card-form'>
            <CentroForm
              empresa={empresa}
              unidades={unidades}
              onSaved={(cc) => nav(`/app/cost-centers/${cc.id}`)}
              onCancel={() => nav('/app/cost-centers')}
            />
          </div>
        ) : (
          <EmptyState icon={<Target />} title='Nenhuma empresa' sub='Cadastre uma empresa primeiro.' />
        )}
      </>
    );
  }

  /* ---- DETAIL ---- */
  if (carregando) return <LoadingState />;
  if (!centro || !empresaDoCentro)
    return (
      <EmptyState icon={<Target />} title='Natureza orçamentária não encontrada' sub='Ele pode ter sido removido.' />
    );

  const todas = unidades.length > 0 && centro.unitIds.length >= unidades.length;
  const cobertura = todas
    ? 'Todas as unidades'
    : centro.unitIds.length === 0
      ? 'Nenhuma unidade'
      : centro.unitIds.map((id) => unidades.find((u) => u.id === id)?.acronym ?? '—').join(', ');

  return (
    <>
      <BackLink fallback='/app/cost-centers' />
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>
            <Target size={20} /> {centro.name}
          </h1>
          <div className='detail-sub'>
            {centro.code} · {empresaDoCentro.legalName}
          </div>
        </div>
        {!editando && canWrite && (
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
            <Info
              label='Auto-aprovação'
              value={centro.autoApprovalLimit > 0 ? `até ${brl(centro.autoApprovalLimit)}` : 'desativada'}
            />
            <Info label='Cobertura' value={cobertura} />
          </div>

          <CostCenterBudgetPanel centro={centro} unidades={unidades} canWrite={canWrite} />

          <CostCenterActivity centro={centro} />
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
