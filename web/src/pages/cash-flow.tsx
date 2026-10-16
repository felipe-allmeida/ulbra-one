import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Controller, useForm } from 'react-hook-form';
import {
  Activity,
  AlertTriangle,
  ChevronRight,
  Landmark,
  ShieldCheck,
  TrendingDown,
  TrendingUp,
  Wallet,
} from 'lucide-react';
import type { CashFlowGranularity, CashFlowPeriod, CashFlowAccountBalance, CashFlowItem } from '../api';
import { useApp } from '../store';
import { useCashFlow, useCashFlowItems, useSetReconciledBalance } from '../queries';
import { brl, brlCompact, moneyInput, parseMoneyInput } from '../lib';
import { getErrorMessage } from '../apiError';
import {
  Button,
  CurrencyInput,
  Drawer,
  EmptyState,
  Field,
  Input,
  Modal,
  Panel,
  Select,
  ToolbarField,
  LoadingState,
} from '../ui';
import { fieldError, reconciledBalanceSchema, resolver, type ReconciledBalanceValues } from './schemas';
import { fmtDate } from '../datetime';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';
import { usePhone } from '../useMediaQuery';

const todayIso = () => new Date().toISOString().slice(0, 10);

const GRANULARITIES: CashFlowGranularity[] = ['Day', 'Week', 'Month'];
const HORIZONS = [3, 6, 12];

/* ============ Hero chart (accumulated balance) ============ */
/**
 * Hand-rolled SVG area chart for the accumulated projected balance. No chart library
 * is in the app's deps, so this is built directly rather than pulling in a heavy one.
 * Past = solid line, future = dashed, both over a teal gradient area fill; the danger
 * zone (below minimum reserve) is shaded; a "today" marker splits past from future and
 * the rupture period (first below reserve) is called out. A hover layer surfaces the
 * closing balance (plus in/out) for the period under the cursor.
 */
function BalanceChart({
  periods,
  minReserve,
  startingBalance,
}: {
  periods: CashFlowPeriod[];
  minReserve: number;
  startingBalance: number;
}) {
  const { t } = useTranslation();
  const [hover, setHover] = useState<number | null>(null);
  if (periods.length === 0) return <div className='help'>{t('cashFlow.chart.empty')}</div>;

  const W = 880;
  const H = 300;
  const padL = 56;
  const padR = 16;
  const padTop = 20;
  const padBottom = 34;
  const plotBottom = H - padBottom;

  // Series: opening of first period (= starting balance), then each closing.
  const points = [startingBalance, ...periods.map((p) => p.closingBalance)];
  const labels = ['', ...periods.map((p) => p.label)];

  const values = [...points, minReserve, 0];
  const rawMax = Math.max(...values);
  const rawMin = Math.min(...values);
  // Pad the range slightly so the line never kisses the top/bottom edges.
  const pad = (rawMax - rawMin || Math.abs(rawMax) || 1) * 0.08;
  const max = rawMax + pad;
  const min = rawMin - pad;
  const range = max - min || 1;

  const x = (i: number) => padL + (i / Math.max(points.length - 1, 1)) * (W - padL - padR);
  const y = (v: number) => padTop + (1 - (v - min) / range) * (plotBottom - padTop);

  // Split into past (solid) and future (dashed). Index 0 is the starting balance (now).
  const lastPastIdx = (() => {
    let idx = 0;
    periods.forEach((p, i) => {
      if (p.isPast) idx = i + 1;
    });
    return idx;
  })();

  const line = (from: number, to: number) =>
    points
      .slice(from, to + 1)
      .map((v, k) => `${k === 0 ? 'M' : 'L'} ${x(from + k).toFixed(1)} ${y(v).toFixed(1)}`)
      .join(' ');

  // Closed area path under the whole curve, for the gradient fill.
  const areaPath =
    points.map((v, i) => `${i === 0 ? 'M' : 'L'} ${x(i).toFixed(1)} ${y(v).toFixed(1)}`).join(' ') +
    ` L ${x(points.length - 1).toFixed(1)} ${plotBottom} L ${x(0).toFixed(1)} ${plotBottom} Z`;

  const reserveY = y(minReserve);
  const ruptureIdx = periods.findIndex((p) => p.belowReserve);
  const todayX = x(lastPastIdx);

  // Horizontal grid + y-axis hints (min / reserve / max).
  const gridLines = [max, minReserve > min ? minReserve : (max + min) / 2, min];

  const hoverPeriod = hover != null && hover >= 1 ? periods[hover - 1] : null;

  return (
    <div className='cf-chart'>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        width='100%'
        height={H}
        role='img'
        aria-label={t('cashFlow.chart.title')}
        preserveAspectRatio='none'
      >
        <defs>
          <linearGradient id='cf-area' x1='0' y1='0' x2='0' y2='1'>
            <stop offset='0%' stopColor='var(--brand)' stopOpacity={0.22} />
            <stop offset='100%' stopColor='var(--brand)' stopOpacity={0} />
          </linearGradient>
        </defs>

        {/* Horizontal grid + y-axis value hints */}
        {gridLines.map((v, i) => (
          <g key={i}>
            <line x1={padL} x2={W - padR} y1={y(v)} y2={y(v)} stroke='var(--border-subtle)' strokeWidth={1} />
            <text x={padL - 8} y={y(v) + 3} fontSize={10} textAnchor='end' fill='var(--muted-foreground)'>
              {brlCompact(v)}
            </text>
          </g>
        ))}

        {/* Danger zone below the minimum reserve */}
        {minReserve > min && (
          <rect
            x={padL}
            y={reserveY}
            width={W - padL - padR}
            height={Math.max(0, plotBottom - reserveY)}
            fill='var(--danger)'
            opacity={0.07}
          />
        )}
        {/* Minimum-reserve line */}
        {minReserve > min && (
          <>
            <line
              x1={padL}
              x2={W - padR}
              y1={reserveY}
              y2={reserveY}
              stroke='var(--danger)'
              strokeDasharray='4 4'
              strokeWidth={1}
              opacity={0.7}
            />
            <text x={W - padR} y={reserveY - 5} fontSize={10} textAnchor='end' fill='var(--danger)'>
              {t('cashFlow.chart.minReserve')}
            </text>
          </>
        )}
        {/* Zero baseline */}
        {min < 0 && max > 0 && (
          <line x1={padL} x2={W - padR} y1={y(0)} y2={y(0)} stroke='var(--border)' strokeWidth={1} />
        )}

        {/* Area fill */}
        <path d={areaPath} fill='url(#cf-area)' />

        {/* Today marker */}
        {lastPastIdx > 0 && lastPastIdx < points.length - 1 && (
          <>
            <line
              x1={todayX}
              x2={todayX}
              y1={padTop}
              y2={plotBottom}
              stroke='var(--muted-foreground)'
              strokeDasharray='3 3'
              strokeWidth={1}
              opacity={0.6}
            />
            <text x={todayX + 4} y={padTop + 9} fontSize={10} textAnchor='start' fill='var(--muted-foreground)'>
              {t('cashFlow.chart.today')}
            </text>
          </>
        )}

        {/* Past (solid) */}
        {lastPastIdx > 0 && <path d={line(0, lastPastIdx)} fill='none' stroke='var(--brand)' strokeWidth={2.25} />}
        {/* Future (dashed) */}
        <path
          d={line(lastPastIdx, points.length - 1)}
          fill='none'
          stroke='var(--brand)'
          strokeWidth={2.25}
          strokeDasharray='6 5'
        />

        {/* Rupture callout */}
        {ruptureIdx >= 0 && (
          <>
            <circle
              cx={x(ruptureIdx + 1)}
              cy={y(points[ruptureIdx + 1])}
              r={5.5}
              fill='var(--danger)'
              stroke='var(--card)'
              strokeWidth={2}
            />
            <text
              x={x(ruptureIdx + 1)}
              y={y(points[ruptureIdx + 1]) - 12}
              fontSize={10}
              fontWeight={600}
              textAnchor='middle'
              fill='var(--danger)'
            >
              {t('cashFlow.chart.rupture')}
            </text>
          </>
        )}

        {/* Hover marker dot */}
        {hover != null && (
          <circle cx={x(hover)} cy={y(points[hover])} r={4} fill='var(--brand)' stroke='var(--card)' strokeWidth={2} />
        )}

        {/* X labels (sparse to avoid clutter) */}
        {labels.map((lbl, i) => {
          const step = Math.ceil(labels.length / 8);
          if (i === 0 || i % step !== 0) return null;
          return (
            <text key={i} x={x(i)} y={H - 10} fontSize={10} textAnchor='middle' fill='var(--muted-foreground)'>
              {lbl}
            </text>
          );
        })}

        {/* Invisible hover targets, one per data point */}
        {points.map((_, i) => {
          const w = (W - padL - padR) / Math.max(points.length - 1, 1);
          return (
            <rect
              key={i}
              x={x(i) - w / 2}
              y={padTop}
              width={w}
              height={plotBottom - padTop}
              fill='transparent'
              onMouseEnter={() => setHover(i)}
              onMouseLeave={() => setHover((h) => (h === i ? null : h))}
            />
          );
        })}
      </svg>

      {/* Hover tooltip */}
      {hover != null && (
        <div
          className='cf-chart-tip'
          style={{ left: `${(x(hover) / W) * 100}%`, top: `${(y(points[hover]) / H) * 100}%` }}
        >
          <div className='cf-chart-tip-label'>{hover === 0 ? t('cashFlow.chart.today') : labels[hover]}</div>
          <div className='cf-chart-tip-value'>{brl(points[hover])}</div>
          {hoverPeriod && (
            <div className='cf-chart-tip-rows'>
              <span className='cf-chart-tip-in'>+{brl(hoverPeriod.inflowActual ?? hoverPeriod.inflowForecast)}</span>
              <span className='cf-chart-tip-out'>−{brl(hoverPeriod.outflowActual ?? hoverPeriod.outflowForecast)}</span>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/* ============ Inflow vs outflow bars ============ */
function FlowBars({ periods }: { periods: CashFlowPeriod[] }) {
  const { t } = useTranslation();
  const max = Math.max(1, ...periods.map((p) => Math.max(p.inflowForecast, p.outflowForecast)));
  return (
    <div className='cf-bars'>
      <div className='cf-bars-legend'>
        <span>
          <i className='cf-dot cf-dot-in' /> {t('cashFlow.bars.inflow')}
        </span>
        <span>
          <i className='cf-dot cf-dot-out' /> {t('cashFlow.bars.outflow')}
        </span>
      </div>
      <div className='cf-bars-grid'>
        {periods.map((p) => {
          const inflow = p.inflowForecast;
          const outflow = p.outflowForecast;
          return (
            <div
              key={p.label}
              className='cf-bar-col'
              title={`${p.label}\n${t('cashFlow.bars.inflow')}: ${brl(inflow)}\n${t('cashFlow.bars.outflow')}: ${brl(outflow)}`}
            >
              <div className='cf-bar-pair'>
                <div
                  className='cf-bar cf-bar-in'
                  style={{ height: `${(inflow / max) * 100}%` }}
                  aria-label={`${t('cashFlow.bars.inflow')} ${brl(inflow)}`}
                />
                <div
                  className='cf-bar cf-bar-out'
                  style={{ height: `${(outflow / max) * 100}%` }}
                  aria-label={`${t('cashFlow.bars.outflow')} ${brl(outflow)}`}
                />
              </div>
              <div className='cf-bar-label'>{p.label}</div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* ============ Drill drawer (items composing a period bucket) ============ */
function DrillDrawer({
  companyId,
  asOf,
  period,
  direction,
  onClose,
}: {
  companyId: string | undefined;
  asOf: string;
  period: CashFlowPeriod;
  direction: 'in' | 'out';
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const items = useCashFlowItems({ companyId, from: period.start, to: period.end, direction, asOf });
  const list = items.data ?? [];
  const total = list.reduce((s, it) => s + it.amount, 0);
  const title =
    direction === 'in'
      ? t('cashFlow.drill.inflow', { period: period.label })
      : t('cashFlow.drill.outflow', { period: period.label });

  return (
    <Drawer open title={title} sub={t('cashFlow.drill.title')} onClose={onClose} width={640}>
      {items.isLoading ? (
        <LoadingState />
      ) : list.length === 0 ? (
        <div className='empty-state' style={{ padding: 24 }}>
          <div className='t'>{t('cashFlow.drill.empty')}</div>
        </div>
      ) : (
        <CashFlowDrillTable items={list} total={total} />
      )}
    </Drawer>
  );
}

/**
 * Ordem = counterparty/document/dueDate/amount, igual a hoje. `counterparty` é o `title`; `document`
 * e `dueDate` são os dois `meta`; `amount` é o único `amount`. Rodapé: `list` aqui NÃO é paginado
 * (o drawer busca o período inteiro de uma vez, sem `Pager`) — `footCell` soma `rows`, que já é
 * exatamente `list`, preservando o mesmo total que o cálculo original (`list.reduce`).
 */
function CashFlowDrillTable({ items, total }: { items: CashFlowItem[]; total: number }) {
  const { t } = useTranslation();
  const columns: Column<CashFlowItem>[] = [
    {
      key: 'counterparty',
      header: t('cashFlow.drill.counterparty'),
      role: 'title',
      cell: (it) => it.counterpartyName || '—',
    },
    { key: 'document', header: t('cashFlow.drill.document'), role: 'meta', cell: (it) => it.documentNumber || '—' },
    {
      key: 'dueDate',
      header: t('cashFlow.drill.dueDate'),
      role: 'meta',
      cell: (it) => <span className='t-mono'>{fmtDate(it.dueDate)}</span>,
    },
    {
      key: 'amount',
      header: t('cashFlow.drill.amount'),
      role: 'amount',
      align: 'right',
      cell: (it) => <span className='amount-cell'>{brl(it.amount)}</span>,
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {brl(total)}
        </span>
      ),
    },
  ];

  return (
    <div className='drawer-table-scroll'>
      <ResponsiveTable
        rows={items}
        columns={columns}
        rowKey={(it) => it.installmentId}
        footLabel={t('cashFlow.drill.total')}
      />
    </div>
  );
}

/* ============ Set reconciled balance modal ============ */
function ReconcileModal({ account, onClose }: { account: CashFlowAccountBalance; onClose: () => void }) {
  const { t } = useTranslation();
  const { control, handleSubmit, formState } = useForm<ReconciledBalanceValues>({
    resolver: resolver(reconciledBalanceSchema),
    mode: 'onChange',
    defaultValues: {
      balance: account.currentBalance != null ? moneyInput(account.currentBalance) : '',
      asOf: account.reconciledAt ?? todayIso(),
    },
  });
  const mutation = useSetReconciledBalance({ onSuccess: () => onClose() });
  const erro = mutation.error ? getErrorMessage(mutation.error) : null;

  const onValid = (v: ReconciledBalanceValues) =>
    mutation.mutate({ bankAccountId: account.bankAccountId, balance: parseMoneyInput(v.balance) ?? 0, asOf: v.asOf });

  return (
    <Modal
      title={t('cashFlow.reconcile.title')}
      sub={t('cashFlow.reconcile.subtitle')}
      onClose={onClose}
      footer={
        <>
          <Button type='button' onClick={onClose} disabled={mutation.isPending}>
            {t('cashFlow.reconcile.cancel')}
          </Button>
          <Button
            type='submit'
            form='reconcile-form'
            variant='brand'
            disabled={!formState.isValid || mutation.isPending}
          >
            {mutation.isPending ? t('cashFlow.reconcile.saving') : t('cashFlow.reconcile.save')}
          </Button>
        </>
      }
    >
      <form id='reconcile-form' onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
        <div className='edit-grid'>
          <Field label={`${t('cashFlow.reconcile.balance')} — ${account.alias}`}>
            <Controller
              control={control}
              name='balance'
              render={({ field }) => (
                <CurrencyInput
                  autoFocus
                  value={field.value}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  name={field.name}
                />
              )}
            />
            {fieldError(formState.errors.balance?.message) && (
              <div className='field-err'>{fieldError(formState.errors.balance?.message)}</div>
            )}
          </Field>
          <Field label={t('cashFlow.reconcile.asOf')}>
            <Controller control={control} name='asOf' render={({ field }) => <Input type='date' {...field} />} />
            {fieldError(formState.errors.asOf?.message) && (
              <div className='field-err'>{fieldError(formState.errors.asOf?.message)}</div>
            )}
          </Field>
        </div>
        {erro && (
          <div className='create-err' style={{ marginTop: 10 }}>
            {erro}
          </div>
        )}
      </form>
    </Modal>
  );
}

/* ============ Page ============ */
/** Sentinel value for the consolidated (whole-group) scope in the company selector. */
const CONSOLIDATED = '__all__';

export function CashFlowPage() {
  const { t } = useTranslation();
  const { empresas } = useApp();
  // Board view: default to the consolidated (whole-group) scope; the user can pick a company.
  const [scope, setScope] = useState<string>(CONSOLIDATED);
  const [granularity, setGranularity] = useState<CashFlowGranularity>('Month');
  const [horizon, setHorizon] = useState(12);
  const [minReserve, setMinReserve] = useState('');
  const [asOf, setAsOf] = useState('');
  const [drill, setDrill] = useState<{ period: CashFlowPeriod; direction: 'in' | 'out' } | null>(null);
  const [reconcile, setReconcile] = useState<CashFlowAccountBalance | null>(null);

  const reserveNum = parseMoneyInput(minReserve) ?? 0;
  const companyId = scope === CONSOLIDATED ? undefined : scope;

  const cashFlow = useCashFlow({
    companyId,
    granularity,
    horizon,
    asOf: asOf || undefined,
    minReserve: reserveNum,
  });
  const report = cashFlow.data;

  const granLabel = (g: CashFlowGranularity) =>
    g === 'Day'
      ? t('cashFlow.granularity.day')
      : g === 'Week'
        ? t('cashFlow.granularity.week')
        : t('cashFlow.granularity.month');

  const lowestLabel = report?.kpis.lowestProjectedPeriodLabel ?? '';
  const belowReserve = report ? report.kpis.lowestProjectedBalance < reserveNum : false;

  const accounts = useMemo(() => report?.accounts ?? [], [report]);

  return (
    <>
      <div className='cf-head'>
        <div className='cf-head-title'>
          <h1 className='page-title'>{t('cashFlow.title')}</h1>
          <p className='page-sub'>{t('cashFlow.subtitle')}</p>
        </div>
        <div className='cf-toolbar'>
          <ToolbarField label={t('cashFlow.controls.company')}>
            <Select value={scope} onChange={(e) => setScope(e.target.value)}>
              <option value={CONSOLIDATED}>{t('cashFlow.consolidated')}</option>
              {empresas.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.legalName}
                </option>
              ))}
            </Select>
          </ToolbarField>
          <ToolbarField label={t('cashFlow.controls.granularity')}>
            <Select value={granularity} onChange={(e) => setGranularity(e.target.value as CashFlowGranularity)}>
              {GRANULARITIES.map((g) => (
                <option key={g} value={g}>
                  {granLabel(g)}
                </option>
              ))}
            </Select>
          </ToolbarField>
          <ToolbarField label={t('cashFlow.controls.horizon')}>
            <Select value={horizon} onChange={(e) => setHorizon(Number(e.target.value))}>
              {HORIZONS.map((h) => (
                <option key={h} value={h}>
                  {t('cashFlow.horizonValue', { count: h })}
                </option>
              ))}
            </Select>
          </ToolbarField>
          <ToolbarField label={t('cashFlow.controls.minReserve')} className='cf-reserve-field'>
            <CurrencyInput value={minReserve} onValueChange={setMinReserve} placeholder='0,00' />
          </ToolbarField>
          <ToolbarField label={t('cashFlow.controls.asOf')}>
            <Input type='date' value={asOf} onChange={(e) => setAsOf(e.target.value)} />
          </ToolbarField>
        </div>
      </div>

      {cashFlow.isLoading && !report ? (
        <LoadingState />
      ) : !report || (report.periods.length === 0 && accounts.length === 0) ? (
        <EmptyState icon={<Activity />} title={t('cashFlow.empty.title')} sub={t('cashFlow.empty.sub')} />
      ) : (
        <>
          <div className='cf-kpis'>
            {/* Primary: cash on hand today — the anchor of the whole projection. */}
            <div className='cf-kpi cf-kpi-primary'>
              <div className='cf-kpi-top'>
                <span className='cf-kpi-label'>{t('cashFlow.kpis.balanceToday')}</span>
                <span className='cf-kpi-icon'>
                  <Wallet size={16} />
                </span>
              </div>
              <div className='cf-kpi-value'>{brl(report.startingBalance)}</div>
            </div>

            {/* Risk: lowest projected balance — danger treatment when below reserve. */}
            <div className={`cf-kpi ${belowReserve ? 'cf-kpi-danger' : 'cf-kpi-ok'}`}>
              <div className='cf-kpi-top'>
                <span className='cf-kpi-label'>{t('cashFlow.kpis.lowestBalance')}</span>
                <span className='cf-kpi-icon'>
                  {belowReserve ? <AlertTriangle size={16} /> : <ShieldCheck size={16} />}
                </span>
              </div>
              <div className='cf-kpi-value'>{brl(report.kpis.lowestProjectedBalance)}</div>
              {lowestLabel && (
                <div className='cf-kpi-foot'>{t('cashFlow.kpis.lowestBalanceAt', { period: lowestLabel })}</div>
              )}
            </div>

            {/* Risk: runway / rupture — danger when it breaches within the horizon. */}
            <div className={`cf-kpi ${report.kpis.runwayPeriods != null ? 'cf-kpi-danger' : 'cf-kpi-ok'}`}>
              <div className='cf-kpi-top'>
                <span className='cf-kpi-label'>{t('cashFlow.kpis.runway')}</span>
                <span className='cf-kpi-icon'>
                  {report.kpis.runwayPeriods != null ? <AlertTriangle size={16} /> : <ShieldCheck size={16} />}
                </span>
              </div>
              <div className='cf-kpi-value'>
                {report.kpis.runwayPeriods != null
                  ? t('cashFlow.kpis.runwayValue', { count: report.kpis.runwayPeriods })
                  : t('cashFlow.kpis.runwayNever')}
              </div>
            </div>

            {/* Net result for the horizon — green/red by sign. */}
            <div className={`cf-kpi ${report.kpis.netResult < 0 ? 'cf-kpi-danger' : 'cf-kpi-success'}`}>
              <div className='cf-kpi-top'>
                <span className='cf-kpi-label'>{t('cashFlow.kpis.periodResult')}</span>
                <span className='cf-kpi-icon'>
                  {report.kpis.netResult < 0 ? <TrendingDown size={16} /> : <TrendingUp size={16} />}
                </span>
              </div>
              <div className='cf-kpi-value'>{brl(report.kpis.netResult)}</div>
            </div>

            {/* Forecast in vs out for the horizon. */}
            <div className='cf-kpi'>
              <div className='cf-kpi-top'>
                <span className='cf-kpi-label'>{t('cashFlow.kpis.inflowForecast')}</span>
                <span className='cf-kpi-icon'>
                  <TrendingUp size={16} />
                </span>
              </div>
              <div className='cf-kpi-value'>{brl(report.kpis.totalInflowForecast)}</div>
              <div className='cf-kpi-foot'>
                {t('cashFlow.kpis.outflowForecast')}: {brl(report.kpis.totalOutflowForecast)}
              </div>
            </div>
          </div>

          {report.overdueReceivable > 0 && (
            <div className='cf-overdue'>
              <span className='cf-overdue-icon'>
                <AlertTriangle size={18} />
              </span>
              <div className='cf-overdue-title'>{t('cashFlow.overdueReceivable.title')}</div>
              <div className='cf-overdue-value'>{brl(report.overdueReceivable)}</div>
              <div className='cf-overdue-desc'>{t('cashFlow.overdueReceivable.description')}</div>
            </div>
          )}

          <div className='cf-stack'>
            <Panel title={t('cashFlow.chart.title')}>
              <BalanceChart periods={report.periods} minReserve={reserveNum} startingBalance={report.startingBalance} />
              <div className='cf-hero-divider' />
              <FlowBars periods={report.periods} />
            </Panel>

            <Panel title={t('cashFlow.periods.title')}>
              <CashFlowPeriodsTable periods={report.periods} onDrill={setDrill} />
            </Panel>

            <Panel title={t('cashFlow.accounts.title')}>
              {accounts.length === 0 ? (
                <div className='help' style={{ padding: 16 }}>
                  {t('cashFlow.accounts.noAccounts')}
                </div>
              ) : (
                <CashFlowAccountsTable accounts={accounts} onReconcile={setReconcile} />
              )}
              {!companyId && accounts.length > 0 && (
                <div className='help' style={{ padding: 16 }}>
                  {t('cashFlow.accounts.consolidatedHint')}
                </div>
              )}
            </Panel>
          </div>
        </>
      )}

      {drill && (
        <DrillDrawer
          companyId={companyId}
          asOf={report?.asOf ?? asOf ?? todayIso()}
          period={drill.period}
          direction={drill.direction}
          onClose={() => setDrill(null)}
        />
      )}
      {reconcile && <ReconcileModal account={reconcile} onClose={() => setReconcile(null)} />}
    </>
  );
}

/**
 * Ordem = period/opening/inflow/outflow/net/closing, igual a hoje. `period` (rótulo + badges de
 * status + "realizado até agora") é o `title` — o mesmo conteúdo composto de sempre, sem coluna
 * própria no desktop original. Cinco valores monetários (opening/inflow/outflow/net/closing) —
 * só um pode ser `amount` (ver aviso de cardinalidade): `closing` já era o único em negrito no
 * desktop original, então é o natural. Os outros quatro vão para `detail`. `rowClassName` preserva o destaque vermelho
 * de `cf-row-danger` (abaixo da reserva mínima) nos dois tamanhos — sem esse prop no primitivo o
 * sinal desaparecia no desktop também.
 *
 * "Realizado até hoje" (entradas/saídas já ocorridas) tinha sido emendado dentro da célula
 * `detail` de `net` (telefone-só) — no card expandido isso rotularia o texto como "Resultado",
 * quando o conteúdo é sobre o realizado, não sobre o resultado projetado (mesmo defeito já
 * rejeitado neste projeto para o rodapé de totais e corrigido em `users.tsx`/commit 4e8a4240).
 * Ganha coluna `detail` própria, só no telefone, com `header` próprio.
 */
function CashFlowPeriodsTable({
  periods,
  onDrill,
}: {
  periods: CashFlowPeriod[];
  onDrill: (d: { period: CashFlowPeriod; direction: 'in' | 'out' }) => void;
}) {
  const { t } = useTranslation();
  // `.rt-title` forces white-space:nowrap + overflow:hidden for its one-line ellipsis truncation
  // (ResponsiveTable.tsx) — inherited by any nested block, so a second free-text line inside title
  // doesn't wrap, it gets silently clipped with no "…" (checked at 390px). The "realizado até
  // agora" line moves to the `net` detail cell on phone only; desktop keeps the original content
  // in `period` untouched (usePhone gates it, not a role change).
  const phone = usePhone();
  const realizedSoFar = (p: CashFlowPeriod) =>
    p.isPast && (p.inflowActual != null || p.outflowActual != null) ? (
      <div className='help cf-period-actual'>
        {t('cashFlow.periods.realizedSoFar', { in: brl(p.inflowActual ?? 0), out: brl(p.outflowActual ?? 0) })}
      </div>
    ) : null;
  const columns: Column<CashFlowPeriod>[] = [
    {
      key: 'period',
      header: t('cashFlow.periods.columns.period'),
      role: 'title',
      cell: (p) => (
        <>
          {p.label}{' '}
          <span className='badge-status badge-pendente'>
            {p.isPast ? t('cashFlow.periods.inProgressTag') : t('cashFlow.periods.forecastTag')}
          </span>
          {p.belowReserve && (
            <span className='badge-status badge-rejeitado' style={{ marginLeft: 6 }}>
              {t('cashFlow.periods.belowReserve')}
            </span>
          )}
          {!phone && realizedSoFar(p)}
        </>
      ),
    },
    {
      key: 'opening',
      header: t('cashFlow.periods.columns.opening'),
      role: 'detail',
      align: 'right',
      cell: (p) => <span className='amount-cell'>{brl(p.openingBalance)}</span>,
    },
    {
      key: 'inflow',
      header: t('cashFlow.periods.columns.inflow'),
      role: 'detail',
      align: 'right',
      cell: (p) => (
        <button type='button' className='amount-cell cf-drill' onClick={() => onDrill({ period: p, direction: 'in' })}>
          <span className='cf-drill-inner'>
            {brl(p.inflowForecast)}
            <ChevronRight size={13} className='cf-drill-caret' />
          </span>
        </button>
      ),
    },
    {
      key: 'outflow',
      header: t('cashFlow.periods.columns.outflow'),
      role: 'detail',
      align: 'right',
      cell: (p) => (
        <button type='button' className='amount-cell cf-drill' onClick={() => onDrill({ period: p, direction: 'out' })}>
          <span className='cf-drill-inner'>
            {brl(p.outflowForecast)}
            <ChevronRight size={13} className='cf-drill-caret' />
          </span>
        </button>
      ),
    },
    {
      key: 'net',
      header: t('cashFlow.periods.columns.net'),
      role: 'detail',
      align: 'right',
      cell: (p) => (
        <span className='amount-cell' style={{ color: p.netForecast < 0 ? 'var(--danger)' : undefined }}>
          {brl(p.netForecast)}
        </span>
      ),
    },
    // Só existe no telefone — no desktop o "realizado até hoje" já mora dentro de `period` (ver
    // acima), e esta coluna nunca é adicionada ao array, então a contagem de colunas do desktop não
    // muda. Tem `header` próprio para que o painel expandido rotule o valor como "Realizado até
    // hoje", não como "Resultado" (ver comentário acima do array).
    ...(phone
      ? [
          {
            key: 'realized',
            header: t('cashFlow.periods.columns.realized'),
            role: 'detail' as const,
            cell: (p: CashFlowPeriod) => realizedSoFar(p),
          },
        ]
      : []),
    {
      key: 'closing',
      header: t('cashFlow.periods.columns.closing'),
      role: 'amount',
      align: 'right',
      cell: (p) => (
        <span className='amount-cell' style={{ fontWeight: 600, color: p.belowReserve ? 'var(--danger)' : undefined }}>
          {brl(p.closingBalance)}
        </span>
      ),
    },
  ];

  return (
    <ResponsiveTable
      rows={periods}
      columns={columns}
      rowKey={(p) => p.label}
      rowClassName={(p) => (p.belowReserve ? 'cf-row-danger' : undefined)}
    />
  );
}

/**
 * Ordem = conta/saldo atual/ação, igual a hoje. O nome da conta (+ ícone + "conciliada em ...") é o
 * `title`; `currentBalance` é o único `amount`. Sem `onRowClick` aqui: a linha nunca navegou, só o
 * botão "Definir saldo" agia.
 *
 * `reconciledLine` (telefone-só) emenda em `action`, que tem `header: ''` — deliberadamente
 * deixado assim: diferente do defeito corrigido acima em `net`/`codeRequester`/`nature`/`bank`
 * /`actions` (um header real descrevendo a coisa errada), aqui não há header nenhum reivindicando
 * nada sobre o conteúdo — `rt-k` fica em branco, então não há rótulo incorreto, só ausência de
 * rótulo. O texto em si já se descreve ("Conciliado em .../Sem âncora de conciliação"). Mais brando
 * que os demais achados; não abre coluna própria.
 */
function CashFlowAccountsTable({
  accounts,
  onReconcile,
}: {
  accounts: CashFlowAccountBalance[];
  onReconcile: (a: CashFlowAccountBalance) => void;
}) {
  const { t } = useTranslation();
  // Same `.rt-title` nowrap/clip constraint as CashFlowPeriodsTable above — the reconciled-at line
  // moves next to the action on phone instead of risking a clip under a long account alias.
  const phone = usePhone();
  const reconciledLine = (a: CashFlowAccountBalance) => (
    <div className='help'>
      {a.reconciledAt
        ? t('cashFlow.accounts.reconciledAt', { date: fmtDate(a.reconciledAt) })
        : t('cashFlow.accounts.notReconciled')}
    </div>
  );
  const columns: Column<CashFlowAccountBalance>[] = [
    {
      key: 'account',
      header: t('nav.bankAccounts'),
      role: 'title',
      cell: (a) => (
        <div className='cf-acct'>
          <span className='cf-acct-icon'>
            <Landmark size={15} />
          </span>
          <div>
            <div className='cf-acct-name'>{a.alias}</div>
            {!phone && reconciledLine(a)}
          </div>
        </div>
      ),
    },
    {
      key: 'currentBalance',
      header: t('cashFlow.accounts.currentBalance'),
      role: 'amount',
      align: 'right',
      cell: (a) => (
        <span className='amount-cell' style={{ fontWeight: 600 }}>
          {brl(a.currentBalance)}
        </span>
      ),
    },
    {
      key: 'action',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (a) => (
        <>
          {phone && reconciledLine(a)}
          <Button onClick={() => onReconcile(a)}>{t('cashFlow.accounts.setBalance')}</Button>
        </>
      ),
    },
  ];

  return <ResponsiveTable rows={accounts} columns={columns} rowKey={(a) => a.bankAccountId} />;
}
