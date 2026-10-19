import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Wallet } from 'lucide-react';
import { type CostCenterBudgetLine, type ProjectMetrics } from '../../api';
import { useApp } from '../../store';
import { useCostCenterBudgetSummary, useProjectMetricsList, useProjects, useUnits } from '../../queries';
import { brl, brlCents } from '../../lib';
import { EmptyState, LoadingState, PageHeader, Select, ToolbarField } from '../../ui';
import { AsideLayout } from '../../components/workflow';
import { usePermissions } from '../../permissions';
import { Bar, BarLegend, money, Tile } from './budget-tiles';
import { ExpandableRow } from './expandable-row';
import { ApproverAside } from './approver-aside';
import { ControllershipAside } from './controllership-aside';
import { currentCompetency, fmtCompetency } from './competency';

/**
 * Approver Budgets (AEL-859, mock "sup orçamentos"): read-only view of the unit budget
 * by nature (monthly) and by project (accumulated ceiling), with expandable rows. Scope by
 * company + unit selector (Consolidado = all — budget-summary accepts an optional unitId).
 */
export function RequisitionBudgetsPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const { controller } = usePermissions();
  const [unitId, setUnitId] = useState('');
  const competency = currentCompetency();

  const units = useUnits(empresaId).data ?? [];
  const budget = useCostCenterBudgetSummary(empresaId, competency, unitId || undefined);
  const projects = useProjects(empresaId).data ?? [];
  const projectUnitFilter = unitId ? projects.filter((p) => p.unitId === unitId) : projects;
  // Metrics for the whole project list in ONE request (aligned to projectUnitFilter order) — no per-row fan-out.
  const projectMetrics = useProjectMetricsList(projectUnitFilter.map((p) => p.id));

  const header = (
    <PageHeader
      title={t('requisitions.budgets.title')}
      sub={t('requisitions.budgets.subtitle', { competency: fmtCompetency(competency) })}
    >
      <ToolbarField label={t('requisitions.fields.unit')}>
        <Select value={unitId} onChange={(e) => setUnitId(e.target.value)} aria-label={t('requisitions.fields.unit')}>
          <option value=''>{t('requisitions.overview.allUnits')}</option>
          {units.map((u) => (
            <option key={u.id} value={u.id}>
              {u.name}
            </option>
          ))}
        </Select>
      </ToolbarField>
    </PageHeader>
  );

  if (!empresa) {
    return (
      <>
        {header}
        <EmptyState
          icon={<Wallet />}
          title={t('requisitions.empty.noCompany')}
          sub={t('requisitions.empty.noCompanySub')}
        />
      </>
    );
  }

  const lines = budget.data ?? [];
  const orcado = lines.reduce((s, l) => s + l.orcado, 0);
  const consumed = lines.reduce((s, l) => s + l.consumed, 0);
  const available = lines.reduce((s, l) => s + l.available, 0);

  return (
    <>
      {header}

      <AsideLayout
        aside={
          controller ? (
            <ControllershipAside empresaId={empresaId} unitId={unitId || undefined} />
          ) : (
            <ApproverAside empresaId={empresaId} unitId={unitId || undefined} />
          )
        }
      >
        {budget.isLoading ? (
          <LoadingState />
        ) : (
          <>
            <div className='rq-list'>
              <div className='xrow-head' style={{ cursor: 'default' }}>
                <span className='rq-row-main xrow-main'>
                  <span className='rq-row-title'>{t('requisitions.budgets.total')}</span>
                  <span className='rq-row-meta'>
                    {t('requisitions.budgets.totalSub', { orcado: brlCents(orcado), consumed: brlCents(consumed) })}
                  </span>
                </span>
                <span className='rq-row-right'>
                  <span className='rq-row-value num'>{brlCents(available)}</span>
                  <span className='xrow-flag calm'>{t('requisitions.budgets.availableFlag')}</span>
                </span>
              </div>
            </div>

            <section className='rq-section'>
              <div className='rq-seclabel'>{t('requisitions.budgets.byNature')}</div>
              {lines.length === 0 ? (
                <div className='rq-empty'>{t('requisitions.budgets.empty')}</div>
              ) : (
                <div className='rq-list'>
                  {lines.map((l) => (
                    <NatureRow key={l.costCenterId} line={l} competency={competency} />
                  ))}
                </div>
              )}
            </section>
          </>
        )}

        {projectUnitFilter.length > 0 && (
          <section className='rq-section'>
            <div className='rq-seclabel'>{t('requisitions.budgets.byProject')}</div>
            <div className='rq-list'>
              {projectUnitFilter.map((p, i) => (
                <ProjectRow key={p.id} name={p.name} metrics={projectMetrics[i]?.data} />
              ))}
            </div>
          </section>
        )}
      </AsideLayout>
    </>
  );
}

/** Expandable row by nature: Orçado/Consumido/Disponível/Uso grid + Pago/Em pagamento bar. */
function NatureRow({ line, competency }: { line: CostCenterBudgetLine; competency: string }) {
  const { t } = useTranslation();
  const over = line.available < 0;
  const pct = line.orcado > 0 ? Math.round((line.consumed / line.orcado) * 100) : 0;
  const tight = !over && line.orcado > 0 && line.available < line.orcado * 0.15;
  const flag = over
    ? { text: t('requisitions.budgets.overFlag'), tone: 'bad' as const }
    : pct >= 75
      ? { text: t('requisitions.budgets.usedPct', { pct }), tone: 'warn' as const }
      : { text: t('requisitions.budgets.availableFlag'), tone: 'calm' as const };

  return (
    <ExpandableRow
      title={line.costCenterName}
      meta={t('requisitions.budgets.natureMeta', {
        orcado: brlCents(line.orcado),
        competency: fmtCompetency(competency),
      })}
      value={brlCents(line.available)}
      flag={flag}
      risk={over ? 'bad' : pct >= 75 ? 'warn' : undefined}
    >
      {line.orcado > 0 ? (
        <>
          <div className='kpitiles'>
            <Tile {...money(line.orcado)} l={t('requisitions.overview.tiles.budgeted')} />
            <Tile {...money(line.consumed)} l={t('requisitions.overview.tiles.consumed')} />
            <Tile
              {...money(line.available)}
              l={t('requisitions.overview.tiles.available')}
              tone={over ? 'bad' : tight ? 'warn' : undefined}
            />
            <Tile n={`${pct}%`} l={t('requisitions.budgets.usage')} />
          </div>
          <div style={{ marginTop: 12 }}>
            <Bar orcado={line.orcado} used={line.realized} queued={line.committed} over={over} tall />
            <BarLegend
              items={[
                { color: 'var(--brand)', label: t('requisitions.overview.legendPaid') },
                { color: 'var(--accent)', label: t('requisitions.overview.legendInPayment') },
                { color: 'var(--muted)', label: t('requisitions.overview.legendAvailable') },
              ]}
            />
          </div>
        </>
      ) : (
        <div className='bud-row-note'>{t('requisitions.budgets.noBudget')}</div>
      )}
    </ExpandableRow>
  );
}

/** Expandable row by project (accumulated ceiling). Metrics come batched from the parent (one request). */
function ProjectRow({ name, metrics }: { name: string; metrics: ProjectMetrics | undefined }) {
  const { t } = useTranslation();
  if (!metrics) {
    return (
      <div className='xrow-head' style={{ cursor: 'default' }}>
        <span className='rq-row-main xrow-main'>
          <span className='rq-row-title'>{name}</span>
        </span>
      </div>
    );
  }

  const over = metrics.isOverCeiling;
  const flag = over
    ? { text: t('requisitions.budgets.projectOverFlag'), tone: 'bad' as const }
    : metrics.percentPrevisto >= 90
      ? { text: t('requisitions.budgets.projectPctFlag', { pct: metrics.percentPrevisto }), tone: 'warn' as const }
      : { text: t('requisitions.budgets.availableFlag'), tone: 'calm' as const };
  const inFlight = Math.max(0, metrics.previsto - metrics.realizado);

  return (
    <ExpandableRow
      title={name}
      meta={t('requisitions.budgets.projectMeta', { limit: brl(metrics.budgetLimit) })}
      value={brl(metrics.saldo)}
      flag={flag}
      risk={over ? 'bad' : metrics.percentPrevisto >= 90 ? 'warn' : undefined}
    >
      <div className='kpitiles'>
        <Tile {...money(metrics.budgetLimit)} l={t('requisitions.projectCard.budgeted')} />
        <Tile {...money(metrics.realizado)} l={t('requisitions.projectCard.consumed')} />
        <Tile
          {...money(metrics.saldo)}
          l={t('requisitions.projectCard.balance')}
          tone={metrics.saldo <= 0 ? 'bad' : undefined}
        />
        <Tile {...money(metrics.previsto)} l={t('requisitions.projectCard.forecast')} tone={over ? 'bad' : undefined} />
      </div>
      <div style={{ marginTop: 12 }}>
        <Bar orcado={metrics.budgetLimit} used={metrics.realizado} queued={inFlight} over={over} tall />
        <BarLegend
          items={[
            { color: 'var(--brand)', label: t('requisitions.budgets.legendConsumed') },
            { color: over ? 'var(--danger)' : 'var(--accent)', label: t('requisitions.budgets.legendInFlight') },
            { color: 'var(--muted)', label: t('requisitions.budgets.legendAvailable') },
          ]}
        />
      </div>
      <div className='bud-row-sub' style={{ marginTop: 10 }}>
        {over
          ? t('requisitions.budgets.projectNoteOver', { amount: brl(metrics.previsto - metrics.budgetLimit) })
          : metrics.percentPrevisto >= 90
            ? t('requisitions.budgets.projectNote90', { pct: metrics.percentPrevisto })
            : t('requisitions.budgets.projectNoteOk')}
      </div>
    </ExpandableRow>
  );
}
