import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ClipboardCheck, Wallet } from 'lucide-react';
import { useApp } from '../../store';
import {
  useCostCenterBudgetSummary,
  useProjectMetricsList,
  useProjects,
  useRequisitions,
  useUnits,
} from '../../queries';
import { brl, brlCents } from '../../lib';
import { Button, EmptyState, LoadingState, PageHeader, Panel, Select, ToolbarField } from '../../ui';
import { AsideLayout } from '../../components/workflow';
import { usePermissions } from '../../permissions';
import { Bar, BarLegend, money, Tile } from './budget-tiles';
import { ApproverAside } from './approver-aside';
import { ControllershipAside } from './controllership-aside';
import { currentCompetency, fmtCompetency } from './competency';

/**
 * Approver "Visão geral" dashboard (AEL-859, mock "sup dashboard"). Only the overview in v1
 * (Gastos/Previsões = phase 2): month tiles, Pago/Em pagamento consumption bar, "Atenção" panel
 * (current-month natures + projects above the ceiling) and an aside with the approval queue + unit
 * budget. Scope: company, with an optional unit selector (Consolidado = all).
 */
export function RequisitionOverviewPage() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { empresa, empresaId } = useApp();
  const { controller } = usePermissions();
  const [unitId, setUnitId] = useState('');
  const competency = currentCompetency();
  // Shared with Approver: Controllership sees the TRIAGE queue as "awaiting you".
  const stageStatus = controller ? 'Triage' : 'Approval';

  const units = useUnits(empresaId).data ?? [];
  const budget = useCostCenterBudgetSummary(empresaId, competency, unitId || undefined);
  const queue = useRequisitions(
    { companyId: empresaId, status: stageStatus, mine: false, unitId: unitId || undefined, page: 1 },
    { enabled: !!empresaId },
  );

  if (!empresa) {
    return (
      <>
        <PageHeader title={t('requisitions.overview.title')} sub={t('requisitions.overview.subtitle')}></PageHeader>
        <EmptyState
          icon={<ClipboardCheck />}
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
  const paid = lines.reduce((s, l) => s + l.realized, 0);
  const inPayment = lines.reduce((s, l) => s + l.committed, 0);
  const usagePct = orcado > 0 ? Math.round((consumed / orcado) * 100) : 0;
  const pending = queue.data?.total ?? 0;

  return (
    <>
      <PageHeader title={t('requisitions.overview.title')} sub={t('requisitions.overview.subtitle')}>
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

      <AsideLayout
        aside={
          controller ? (
            <ControllershipAside empresaId={empresaId} unitId={unitId || undefined} />
          ) : (
            <ApproverAside empresaId={empresaId} unitId={unitId || undefined} />
          )
        }
      >
        <div className='kpitiles'>
          <Tile {...money(orcado)} l={t('requisitions.overview.tiles.budgeted')} />
          <Tile {...money(consumed)} l={t('requisitions.overview.tiles.consumed')} />
          <Tile
            {...money(available)}
            l={t('requisitions.overview.tiles.available')}
            tone={available < 0 ? 'bad' : undefined}
          />
          <Tile
            n={String(pending)}
            l={t('requisitions.overview.tiles.awaitingYou')}
            tone={pending > 0 ? 'warn' : undefined}
          />
        </div>

        <div className='detail-stack' style={{ marginTop: 14 }}>
          <Panel title={t('requisitions.overview.consumption', { competency: fmtCompetency(competency) })} padded>
            <Bar orcado={orcado} used={paid} queued={inPayment} over={consumed > orcado} tall />
            <BarLegend
              items={[
                { color: 'var(--brand)', label: t('requisitions.overview.legendPaid') },
                { color: 'var(--accent)', label: t('requisitions.overview.legendInPayment') },
                { color: 'var(--muted)', label: t('requisitions.overview.legendAvailable') },
              ]}
            />
            <div className='bud-row-sub' style={{ marginTop: 10 }}>
              {t('requisitions.overview.numbers', {
                consumed: brlCents(consumed),
                available: brlCents(available),
                pct: usagePct,
              })}
            </div>
          </Panel>

          <Panel title={t('requisitions.overview.attentionTitle')} padded>
            <AttentionList empresaId={empresaId} unitId={unitId || undefined} competency={competency} />
          </Panel>

          <Panel title={t('requisitions.overview.seeMore')} padded>
            <Button icon={<Wallet />} onClick={() => nav('/app/requisitions/budgets')}>
              {t('requisitions.overview.openBudgets')}
            </Button>
          </Panel>
        </div>
      </AsideLayout>
    </>
  );
}

/** Current-month alerts by nature + projects above the ceiling (N+1 accepted — few projects). */
function AttentionList({ empresaId, unitId, competency }: { empresaId: string; unitId?: string; competency: string }) {
  const { t } = useTranslation();
  const budget = useCostCenterBudgetSummary(empresaId, competency, unitId);
  const lines = budget.data ?? [];
  const projects = useProjects(empresaId).data ?? [];
  // Batch metrics → lets us know how many projects overflowed (the "none" message needs this).
  const metrics = useProjectMetricsList(projects.map((p) => p.id));
  const overProjects = metrics.map((m) => m.data).filter((m) => m?.isOverCeiling);

  if (budget.isLoading) return <LoadingState />;

  const alerts: { key: string; tone: 'bad' | 'warn'; text: string }[] = [];
  for (const l of lines) {
    if (l.available < 0) {
      alerts.push({
        key: `over-${l.costCenterId}`,
        tone: 'bad',
        text: t('requisitions.overview.attention.natureOver', {
          name: l.costCenterName,
          amount: brlCents(-l.available),
        }),
      });
    } else if (l.orcado > 0 && l.consumed / l.orcado >= 0.85) {
      alerts.push({
        key: `usage-${l.costCenterId}`,
        tone: 'warn',
        text: t('requisitions.overview.attention.nature85', {
          name: l.costCenterName,
          pct: Math.round((l.consumed / l.orcado) * 100),
        }),
      });
    } else if (l.forecast > l.available) {
      alerts.push({
        key: `queue-${l.costCenterId}`,
        tone: 'warn',
        text: t('requisitions.overview.attention.natureQueue', {
          name: l.costCenterName,
          forecast: brlCents(l.forecast),
          available: brlCents(l.available),
        }),
      });
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {alerts.map((a) => (
        <div key={a.key} className='proj-alert' role='alert' style={a.tone === 'warn' ? WARN_ALERT : undefined}>
          <span>{a.text}</span>
        </div>
      ))}
      {overProjects.map((m) => (
        <div key={m!.id} className='proj-alert' role='alert'>
          <span>
            {t('requisitions.overview.attention.projectOver', {
              name: m!.name,
              amount: brl(m!.previsto - m!.budgetLimit),
            })}
          </span>
        </div>
      ))}
      {alerts.length === 0 && overProjects.length === 0 && (
        <div className='bud-row-note'>{t('requisitions.overview.attention.none')}</div>
      )}
    </div>
  );
}

const WARN_ALERT = {
  background: 'var(--warning-subtle)',
  borderColor: 'var(--warning)',
  color: 'var(--warning)',
} as const;
