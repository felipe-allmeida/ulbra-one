import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Controller, useForm } from 'react-hook-form';
import { Activity, Landmark } from 'lucide-react';
import type { CashFlowGranularity, CashFlowPeriod, CashFlowAccountBalance } from '../api';
import { useApp } from '../store';
import { useCashFlow, useCashFlowItems, useSetReconciledBalance } from '../queries';
import { brl } from '../lib';
import { getErrorMessage } from '../apiError';
import { Button, Drawer, EmptyState, Field, Input, Kpi, Modal, Panel, Select, ToolbarField } from '../ui';
import { fieldError, reconciledBalanceSchema, resolver, type ReconciledBalanceValues } from './schemas';

const fmtDate = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');
const todayIso = () => new Date().toISOString().slice(0, 10);

const GRANULARITIES: CashFlowGranularity[] = ['Day', 'Week', 'Month'];
const HORIZONS = [3, 6, 12];

/* ============ Hero chart (accumulated balance) ============ */
/**
 * Lightweight SVG line chart for the accumulated projected balance. No chart library
 * is in the app's deps, so a hand-rolled SVG is used rather than pulling in a heavy one.
 * Past = solid line, future = dashed; danger zone (below minimum reserve) is shaded;
 * the rupture period (first below reserve) is marked.
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
  if (periods.length === 0) return <div className='help'>{t('cashFlow.chart.empty')}</div>;

  const W = 760;
  const H = 240;
  const padX = 8;
  const padTop = 16;
  const padBottom = 28;

  // Series: opening of first period (= starting balance), then each closing.
  const points = [startingBalance, ...periods.map((p) => p.closingBalance)];
  const labels = ['', ...periods.map((p) => p.label)];

  const values = [...points, minReserve];
  const max = Math.max(...values);
  const min = Math.min(...values);
  const range = max - min || 1;

  const x = (i: number) => padX + (i / Math.max(points.length - 1, 1)) * (W - padX * 2);
  const y = (v: number) => padTop + (1 - (v - min) / range) * (H - padTop - padBottom);

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

  const reserveY = y(minReserve);
  const ruptureIdx = periods.findIndex((p) => p.belowReserve);

  return (
    <svg viewBox={`0 0 ${W} ${H}`} width='100%' height={H} role='img' aria-label={t('cashFlow.chart.title')}>
      {/* Danger zone below the minimum reserve */}
      {minReserve > min && (
        <rect
          x={padX}
          y={reserveY}
          width={W - padX * 2}
          height={Math.max(0, H - padBottom - reserveY)}
          fill='var(--danger)'
          opacity={0.08}
        />
      )}
      {/* Minimum-reserve line */}
      <line
        x1={padX}
        x2={W - padX}
        y1={reserveY}
        y2={reserveY}
        stroke='var(--danger)'
        strokeDasharray='4 4'
        strokeWidth={1}
        opacity={0.6}
      />
      {/* Zero baseline */}
      {min < 0 && max > 0 && (
        <line x1={padX} x2={W - padX} y1={y(0)} y2={y(0)} stroke='var(--border)' strokeWidth={1} />
      )}
      {/* Past (solid) */}
      {lastPastIdx > 0 && <path d={line(0, lastPastIdx)} fill='none' stroke='var(--brand)' strokeWidth={2} />}
      {/* Future (dashed) */}
      <path
        d={line(lastPastIdx, points.length - 1)}
        fill='none'
        stroke='var(--brand)'
        strokeWidth={2}
        strokeDasharray='6 5'
      />
      {/* Rupture marker */}
      {ruptureIdx >= 0 && <circle cx={x(ruptureIdx + 1)} cy={y(points[ruptureIdx + 1])} r={5} fill='var(--danger)' />}
      {/* X labels (sparse to avoid clutter) */}
      {labels.map((lbl, i) => {
        const step = Math.ceil(labels.length / 8);
        if (i === 0 || i % step !== 0) return null;
        return (
          <text key={i} x={x(i)} y={H - 8} fontSize={10} textAnchor='middle' fill='var(--muted)'>
            {lbl}
          </text>
        );
      })}
    </svg>
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
        {periods.map((p) => (
          <div key={p.label} className='cf-bar-col' title={p.label}>
            <div className='cf-bar-pair'>
              <div
                className='cf-bar cf-bar-in'
                style={{ height: `${(p.inflowForecast / max) * 100}%` }}
                aria-label={`${t('cashFlow.bars.inflow')} ${brl(p.inflowForecast)}`}
              />
              <div
                className='cf-bar cf-bar-out'
                style={{ height: `${(p.outflowForecast / max) * 100}%` }}
                aria-label={`${t('cashFlow.bars.outflow')} ${brl(p.outflowForecast)}`}
              />
            </div>
            <div className='cf-bar-label'>{p.label}</div>
          </div>
        ))}
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
        <div className='help'>{t('cashFlow.loading')}</div>
      ) : list.length === 0 ? (
        <div className='empty-state' style={{ padding: 24 }}>
          <div className='t'>{t('cashFlow.drill.empty')}</div>
        </div>
      ) : (
        <table>
          <thead>
            <tr>
              <th>{t('cashFlow.drill.counterparty')}</th>
              <th>{t('cashFlow.drill.document')}</th>
              <th>{t('cashFlow.drill.dueDate')}</th>
              <th style={{ textAlign: 'right' }}>{t('cashFlow.drill.amount')}</th>
            </tr>
          </thead>
          <tbody>
            {list.map((it) => (
              <tr key={it.installmentId}>
                <td>{it.counterpartyName || '—'}</td>
                <td>{it.documentNumber || '—'}</td>
                <td className='t-mono'>{fmtDate(it.dueDate)}</td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brl(it.amount)}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td colSpan={3} style={{ fontWeight: 600 }}>
                {t('cashFlow.drill.total')}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                {brl(total)}
              </td>
            </tr>
          </tfoot>
        </table>
      )}
    </Drawer>
  );
}

/* ============ Set reconciled balance modal ============ */
function ReconcileModal({ account, onClose }: { account: CashFlowAccountBalance; onClose: () => void }) {
  const { t } = useTranslation();
  const { control, handleSubmit, formState } = useForm<ReconciledBalanceValues>({
    resolver: resolver(reconciledBalanceSchema),
    mode: 'onChange',
    defaultValues: { balance: String(account.currentBalance ?? ''), asOf: account.reconciledAt ?? todayIso() },
  });
  const mutation = useSetReconciledBalance({ onSuccess: () => onClose() });
  const erro = mutation.error ? getErrorMessage(mutation.error) : null;

  const onValid = (v: ReconciledBalanceValues) =>
    mutation.mutate({ bankAccountId: account.bankAccountId, balance: Number(v.balance), asOf: v.asOf });

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
              render={({ field }) => <Input autoFocus inputMode='decimal' {...field} placeholder='0' />}
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
  const { empresas, empresaId } = useApp();
  // Scope defaults to the globally-selected company; the user can switch to consolidated.
  const [scope, setScope] = useState<string>(() => empresaId || CONSOLIDATED);
  const [granularity, setGranularity] = useState<CashFlowGranularity>('Month');
  const [horizon, setHorizon] = useState(12);
  const [minReserve, setMinReserve] = useState('');
  const [asOf, setAsOf] = useState('');
  const [drill, setDrill] = useState<{ period: CashFlowPeriod; direction: 'in' | 'out' } | null>(null);
  const [reconcile, setReconcile] = useState<CashFlowAccountBalance | null>(null);

  const reserveNum = minReserve.trim() === '' ? 0 : Number(minReserve) || 0;
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
      <div className='page-head'>
        <div>
          <h1 className='page-title'>{t('cashFlow.title')}</h1>
          <p className='page-sub'>{t('cashFlow.subtitle')}</p>
        </div>
        <div className='toolbar'>
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
          <ToolbarField label={t('cashFlow.controls.minReserve')}>
            <Input
              inputMode='decimal'
              value={minReserve}
              onChange={(e) => setMinReserve(e.target.value)}
              placeholder='0'
              style={{ width: 120 }}
            />
          </ToolbarField>
          <ToolbarField label={t('cashFlow.controls.asOf')}>
            <Input type='date' value={asOf} onChange={(e) => setAsOf(e.target.value)} />
          </ToolbarField>
        </div>
      </div>

      {cashFlow.isLoading && !report ? (
        <div className='help'>{t('cashFlow.loading')}</div>
      ) : !report || (report.periods.length === 0 && accounts.length === 0) ? (
        <EmptyState icon={<Activity />} title={t('cashFlow.empty.title')} sub={t('cashFlow.empty.sub')} />
      ) : (
        <>
          <div className='kpi-grid' style={{ gridTemplateColumns: 'repeat(5, 1fr)' }}>
            <Kpi label={t('cashFlow.kpis.balanceToday')} value={brl(report.startingBalance)} />
            <div className='kpi-card' style={{ borderColor: belowReserve ? 'var(--danger)' : undefined }}>
              <div className='kpi-label'>{t('cashFlow.kpis.lowestBalance')}</div>
              <div className='kpi-value' style={{ color: belowReserve ? 'var(--danger)' : undefined }}>
                {brl(report.kpis.lowestProjectedBalance)}
              </div>
              {lowestLabel && (
                <div className='kpi-foot'>{t('cashFlow.kpis.lowestBalanceAt', { period: lowestLabel })}</div>
              )}
            </div>
            <div
              className='kpi-card'
              style={{ borderColor: report.kpis.runwayPeriods != null ? 'var(--warning)' : undefined }}
            >
              <div className='kpi-label'>{t('cashFlow.kpis.runway')}</div>
              <div
                className='kpi-value'
                style={{ color: report.kpis.runwayPeriods != null ? 'var(--danger)' : undefined }}
              >
                {report.kpis.runwayPeriods != null
                  ? t('cashFlow.kpis.runwayValue', { count: report.kpis.runwayPeriods })
                  : t('cashFlow.kpis.runwayNever')}
              </div>
            </div>
            <div className='kpi-card'>
              <div className='kpi-label'>{t('cashFlow.kpis.periodResult')}</div>
              <div
                className='kpi-value'
                style={{ color: report.kpis.netResult < 0 ? 'var(--danger)' : 'var(--success)' }}
              >
                {brl(report.kpis.netResult)}
              </div>
            </div>
            <div className='kpi-card'>
              <div className='kpi-label'>{t('cashFlow.kpis.inflowForecast')}</div>
              <div className='kpi-value'>{brl(report.kpis.totalInflowForecast)}</div>
              <div className='kpi-foot'>
                {t('cashFlow.kpis.outflowForecast')}: {brl(report.kpis.totalOutflowForecast)}
              </div>
            </div>
          </div>

          {report.overdueReceivable > 0 && (
            <div className='cf-overdue'>
              <div className='cf-overdue-title'>{t('cashFlow.overdueReceivable.title')}</div>
              <div className='cf-overdue-value'>{brl(report.overdueReceivable)}</div>
              <div className='cf-overdue-desc'>{t('cashFlow.overdueReceivable.description')}</div>
            </div>
          )}

          <Panel title={t('cashFlow.chart.title')}>
            <BalanceChart periods={report.periods} minReserve={reserveNum} startingBalance={report.startingBalance} />
          </Panel>

          <Panel title={t('cashFlow.bars.title')}>
            <FlowBars periods={report.periods} />
          </Panel>

          <Panel title={t('cashFlow.periods.title')}>
            <table>
              <thead>
                <tr>
                  <th>{t('cashFlow.periods.columns.period')}</th>
                  <th style={{ textAlign: 'right' }}>{t('cashFlow.periods.columns.opening')}</th>
                  <th style={{ textAlign: 'right' }}>{t('cashFlow.periods.columns.inflow')}</th>
                  <th style={{ textAlign: 'right' }}>{t('cashFlow.periods.columns.outflow')}</th>
                  <th style={{ textAlign: 'right' }}>{t('cashFlow.periods.columns.net')}</th>
                  <th style={{ textAlign: 'right' }}>{t('cashFlow.periods.columns.closing')}</th>
                </tr>
              </thead>
              <tbody>
                {report.periods.map((p) => (
                  <tr key={p.label} className={p.belowReserve ? 'cf-row-danger' : undefined}>
                    <td>
                      {p.label}{' '}
                      <span className={`badge-status ${p.isPast ? 'badge-pago' : 'badge-pendente'}`}>
                        {p.isPast ? t('cashFlow.periods.actualTag') : t('cashFlow.periods.forecastTag')}
                      </span>
                      {p.belowReserve && (
                        <span className='badge-status badge-rejeitado' style={{ marginLeft: 6 }}>
                          {t('cashFlow.periods.belowReserve')}
                        </span>
                      )}
                    </td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {brl(p.openingBalance)}
                    </td>
                    <td
                      className='amount-cell'
                      style={{ textAlign: 'right', cursor: 'pointer', textDecoration: 'underline dotted' }}
                      onClick={() => setDrill({ period: p, direction: 'in' })}
                    >
                      {brl(p.inflowActual ?? p.inflowForecast)}
                    </td>
                    <td
                      className='amount-cell'
                      style={{ textAlign: 'right', cursor: 'pointer', textDecoration: 'underline dotted' }}
                      onClick={() => setDrill({ period: p, direction: 'out' })}
                    >
                      {brl(p.outflowActual ?? p.outflowForecast)}
                    </td>
                    <td
                      className='amount-cell'
                      style={{ textAlign: 'right', color: p.netForecast < 0 ? 'var(--danger)' : undefined }}
                    >
                      {brl(p.netForecast)}
                    </td>
                    <td
                      className='amount-cell'
                      style={{
                        textAlign: 'right',
                        fontWeight: 600,
                        color: p.belowReserve ? 'var(--danger)' : undefined,
                      }}
                    >
                      {brl(p.closingBalance)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Panel>

          <Panel title={t('cashFlow.accounts.title')}>
            {accounts.length === 0 ? (
              <div className='help' style={{ padding: 16 }}>
                {t('cashFlow.accounts.noAccounts')}
              </div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>{t('nav.bankAccounts')}</th>
                    <th style={{ textAlign: 'right' }}>{t('cashFlow.accounts.currentBalance')}</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {accounts.map((a) => (
                    <tr key={a.bankAccountId}>
                      <td>
                        <Landmark size={14} style={{ verticalAlign: 'middle', marginRight: 6 }} />
                        {a.alias}
                        <div className='help'>
                          {a.reconciledAt
                            ? t('cashFlow.accounts.reconciledAt', { date: fmtDate(a.reconciledAt) })
                            : t('cashFlow.accounts.notReconciled')}
                        </div>
                      </td>
                      <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                        {brl(a.currentBalance)}
                      </td>
                      <td style={{ textAlign: 'right' }}>
                        <Button onClick={() => setReconcile(a)}>{t('cashFlow.accounts.setBalance')}</Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            {!companyId && accounts.length > 0 && (
              <div className='help' style={{ padding: 16 }}>
                {t('cashFlow.accounts.consolidatedHint')}
              </div>
            )}
          </Panel>
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
