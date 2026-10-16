import { useMemo, useState } from 'react';
import { useOriginNav } from '../../nav';
import { useTranslation } from 'react-i18next';
import { Gauge } from 'lucide-react';
import { useApp } from '../../store';
import { useSlaReport, useUnits } from '../../queries';
import type { SlaOverdueRow, SlaStageStat, SlaUnitStat } from '../../api';
import { brl } from '../../lib';
import { EmptyState, PageHeader, Panel, Select, ToolbarField, Input, LoadingState } from '../../ui';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { Tile } from './budget-tiles';
import { RequisitionStatusBadge } from './badges';

const isoDate = (d: Date) => d.toLocaleDateString('en-CA'); // YYYY-MM-DD in local tz (BRT)
const fmtH = (h: number) => `${h.toLocaleString('pt-BR', { maximumFractionDigits: 1 })}h`;
const fmtPct = (p: number) => `${p.toLocaleString('pt-BR', { maximumFractionDigits: 0 })}%`;

/**
 * SLA Analysis (AEL-872) — ADM + Approver screen (gated by requisitions:sla-report on the route/menu).
 * Live queue (requisitions currently overdue in Triage/Approval) + period history (average time per
 * stage, % within SLA, cycle, ranking by unit). Consumes GET /requisitions/sla-report; the business hours
 * (BR holidays) are computed in the backend, so the screen only displays.
 */
export function RequisitionSlaReportPage() {
  const { t } = useTranslation();
  const openDetail = useOriginNav();
  const { empresa, empresaId } = useApp();
  const [unitId, setUnitId] = useState('');
  const [from, setFrom] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() - 90);
    return isoDate(d);
  });
  const [to, setTo] = useState(() => isoDate(new Date()));

  const unitsQuery = useUnits(empresaId);
  const units = unitsQuery.data ?? [];
  const unitName = useMemo(() => new Map((unitsQuery.data ?? []).map((u) => [u.id, u.name])), [unitsQuery.data]);

  const report = useSlaReport(
    { companyId: empresaId, unitId: unitId || undefined, from, to },
    { enabled: !!empresaId },
  );

  if (!empresa) {
    return (
      <>
        <PageHeader title={t('requisitions.slaReport.title')} sub={t('requisitions.slaReport.subtitle')} />
        <EmptyState
          icon={<Gauge />}
          title={t('requisitions.empty.noCompany')}
          sub={t('requisitions.empty.noCompanySub')}
        />
      </>
    );
  }

  // Fila ao vivo: código é `meta` (identificador curto), descrição é a única `title`; unidade é o
  // segundo `meta` (2 é o teto — ver cardinalidade em ResponsiveTable.tsx). estágio e o chip de atraso
  // são ambos badges/chips → `status` (empilham); elapsed (número puro, sem selo) fica em `detail`;
  // valor é o único `amount`. Ordem = a ordem já renderizada hoje.
  const liveColumns: Column<SlaOverdueRow>[] = [
    { key: 'code', header: t('requisitions.slaReport.cols.code'), role: 'meta', cell: (r) => `#${r.code}` },
    {
      key: 'description',
      header: t('requisitions.slaReport.cols.description'),
      role: 'title',
      cell: (r) => r.description,
    },
    {
      key: 'stage',
      header: t('requisitions.slaReport.cols.stage'),
      role: 'status',
      cell: (r) => <RequisitionStatusBadge s={r.stage} />,
    },
    {
      key: 'unit',
      header: t('requisitions.slaReport.cols.unit'),
      role: 'meta',
      cell: (r) => unitName.get(r.unitId) ?? '—',
    },
    {
      key: 'overdue',
      header: t('requisitions.slaReport.cols.overdue'),
      role: 'status',
      cell: (r) => <span className='sla-chip sla-danger'>{fmtH(r.overdueByHours)}</span>,
    },
    {
      key: 'elapsed',
      header: t('requisitions.slaReport.cols.elapsed'),
      role: 'detail',
      cell: (r) => fmtH(r.businessHoursElapsed),
    },
    { key: 'value', header: t('requisitions.slaReport.cols.value'), role: 'amount', cell: (r) => brl(r.estimateValue) },
  ];

  // Histórico por estágio: relatório agregado, sem clique de linha (mesmo padrão de
  // by-cost-center-table.tsx/by-guia-table.tsx) — estágio (o selo) é a única `title`, o resto é `detail`
  // (nenhum valor monetário aqui, então não há `amount`).
  const stageColumns: Column<SlaStageStat>[] = [
    {
      key: 'stage',
      header: t('requisitions.slaReport.cols.stage'),
      role: 'title',
      cell: (s) => <RequisitionStatusBadge s={s.stage} />,
    },
    {
      key: 'occurrences',
      header: t('requisitions.slaReport.hist.occurrences'),
      role: 'detail',
      cell: (s) => s.occurrences,
    },
    {
      key: 'avgHours',
      header: t('requisitions.slaReport.hist.avgHours'),
      role: 'detail',
      cell: (s) => (s.occurrences > 0 ? fmtH(s.averageBusinessHours) : '—'),
    },
    {
      key: 'withinSla',
      header: t('requisitions.slaReport.hist.withinSla'),
      role: 'detail',
      cell: (s) => (s.occurrences > 0 ? `${s.withinSlaCount}/${s.occurrences} (${fmtPct(s.withinSlaPercent)})` : '—'),
    },
    {
      key: 'threshold',
      header: t('requisitions.slaReport.hist.threshold'),
      role: 'detail',
      cell: (s) => fmtH(s.thresholdHours),
    },
  ];

  // Ranking por unidade: mesmo padrão — unidade (nome, texto puro) é a única `title`; sem clique de linha.
  const byUnitColumns: Column<SlaUnitStat>[] = [
    {
      key: 'unit',
      header: t('requisitions.slaReport.cols.unit'),
      role: 'title',
      cell: (u) => unitName.get(u.unitId) ?? '—',
    },
    {
      key: 'triage',
      header: t('requisitions.slaReport.hist.unitTriage'),
      role: 'detail',
      cell: (u) =>
        u.triageOccurrences > 0
          ? `${fmtH(u.avgTriageHours)} · ${fmtPct(u.triageWithinSlaPercent)} (${u.triageOccurrences})`
          : '—',
    },
    {
      key: 'approval',
      header: t('requisitions.slaReport.hist.unitApproval'),
      role: 'detail',
      cell: (u) =>
        u.approvalOccurrences > 0
          ? `${fmtH(u.avgApprovalHours)} · ${fmtPct(u.approvalWithinSlaPercent)} (${u.approvalOccurrences})`
          : '—',
    },
    {
      key: 'cycle',
      header: t('requisitions.slaReport.hist.unitCycle'),
      role: 'detail',
      cell: (u) => (u.cycleCount > 0 ? `${fmtH(u.avgCycleHours)} (${u.cycleCount})` : '—'),
    },
  ];

  const data = report.data;
  const stageOf = (s: SlaStageStat['stage']) => data?.stages.find((x) => x.stage === s);
  const triage = stageOf('Triage');
  const approval = stageOf('Approval');
  const totalOcc = (data?.stages ?? []).reduce((s, x) => s + x.occurrences, 0);
  const totalWithin = (data?.stages ?? []).reduce((s, x) => s + x.withinSlaCount, 0);
  const overallPct = totalOcc > 0 ? (100 * totalWithin) / totalOcc : 0;

  return (
    <>
      <PageHeader title={t('requisitions.slaReport.title')} sub={t('requisitions.slaReport.subtitle')}>
        <ToolbarField label={t('requisitions.fields.unit')}>
          <Select value={unitId} onChange={(e) => setUnitId(e.target.value)} aria-label={t('requisitions.fields.unit')}>
            <option value=''>{t('requisitions.slaReport.allUnits')}</option>
            {units.map((u) => (
              <option key={u.id} value={u.id}>
                {u.name}
              </option>
            ))}
          </Select>
        </ToolbarField>
        <ToolbarField label={t('requisitions.slaReport.from')}>
          <Input type='date' value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
        </ToolbarField>
        <ToolbarField label={t('requisitions.slaReport.to')}>
          <Input type='date' value={to} min={from} onChange={(e) => setTo(e.target.value)} />
        </ToolbarField>
      </PageHeader>

      {!data ? (
        report.isError || report.isPaused ? (
          <EmptyState
            icon={<Gauge />}
            title={t('requisitions.slaReport.errorTitle')}
            sub={t('requisitions.slaReport.errorSub')}
          />
        ) : (
          <LoadingState />
        )
      ) : (
        <>
          {/* ---- Live queue ---- */}
          <div className='kpitiles'>
            <Tile
              n={String(data.live.totalOverdue)}
              l={t('requisitions.slaReport.tiles.totalOverdue')}
              tone={data.live.totalOverdue > 0 ? 'bad' : undefined}
            />
            <Tile n={String(data.live.triageOverdue)} l={t('requisitions.slaReport.tiles.triageOverdue')} />
            <Tile n={String(data.live.approvalOverdue)} l={t('requisitions.slaReport.tiles.approvalOverdue')} />
            <Tile n={fmtH(data.live.oldestOverdueBusinessHours)} l={t('requisitions.slaReport.tiles.oldest')} />
          </div>

          <div className='detail-stack' style={{ marginTop: 14 }}>
            <Panel title={t('requisitions.slaReport.live.title')}>
              {data.live.rows.length === 0 ? (
                <div className='panel-body'>
                  <div className='bud-row-note'>{t('requisitions.slaReport.live.empty')}</div>
                </div>
              ) : (
                <ResponsiveTable
                  rows={data.live.rows}
                  columns={liveColumns}
                  rowKey={(r) => r.requisitionId}
                  onRowClick={(r) => {
                    void openDetail(`/app/requisitions/${r.requisitionId}`);
                  }}
                />
              )}
            </Panel>

            {/* ---- History ---- */}
            <div className='kpitiles'>
              <Tile
                n={triage ? fmtH(triage.averageBusinessHours) : '—'}
                l={t('requisitions.slaReport.tiles.avgTriage')}
              />
              <Tile
                n={approval ? fmtH(approval.averageBusinessHours) : '—'}
                l={t('requisitions.slaReport.tiles.avgApproval')}
              />
              <Tile
                n={data.cycle.count > 0 ? fmtH(data.cycle.averageBusinessHours) : '—'}
                l={t('requisitions.slaReport.tiles.cycle', { count: data.cycle.count })}
              />
              <Tile
                n={totalOcc > 0 ? fmtPct(overallPct) : '—'}
                l={t('requisitions.slaReport.tiles.withinSla')}
                tone={totalOcc > 0 && overallPct < 100 ? 'warn' : undefined}
              />
            </div>

            <Panel title={t('requisitions.slaReport.hist.stageTitle')}>
              <ResponsiveTable rows={data.stages ?? []} columns={stageColumns} rowKey={(s) => s.stage} />
            </Panel>

            <Panel title={t('requisitions.slaReport.hist.byUnitTitle')}>
              {data.byUnit.length === 0 ? (
                <div className='panel-body'>
                  <div className='bud-row-note'>{t('requisitions.slaReport.hist.empty')}</div>
                </div>
              ) : (
                <ResponsiveTable rows={data.byUnit} columns={byUnitColumns} rowKey={(u) => u.unitId} />
              )}
            </Panel>
          </div>
        </>
      )}
    </>
  );
}
