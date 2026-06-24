import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { CalendarClock } from 'lucide-react';
import { useApp } from '../store';
import { useAging, useAgingItems, useCostCenters } from '../queries';
import { brl } from '../lib';
import { Drawer, EmptyState, Kpi, Panel, Select, ToolbarField } from '../ui';
import { EmpresaPicker } from './shared';
import { fmtData } from '../datetime';

type PivotMode = 'vendor' | 'costCenter';

const BUCKET_KEYS = ['notYetDue', 'overdue1To30', 'overdue31To60', 'overdue61To90', 'overdue90Plus'] as const;

/** Row shape shared by both pivots after normalization. */
interface PivotRow {
  id: string;
  name: string;
  notYetDue: number;
  overdue1To30: number;
  overdue31To60: number;
  overdue61To90: number;
  overdue90Plus: number;
  total: number;
  vendorId?: string;
  costCenterId?: string;
}

/* ============ Drill drawer ============ */
function DrillDrawer({
  companyId,
  row,
  mode,
  onClose,
}: {
  companyId: string;
  row: PivotRow;
  mode: PivotMode;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const params =
    mode === 'vendor' ? { companyId, vendorId: row.vendorId } : { companyId, costCenterId: row.costCenterId };
  const items = useAgingItems(params, { enabled: !!companyId });
  const list = items.data ?? [];

  return (
    <Drawer open title={row.name} sub={t('aging.subtitle')} onClose={onClose} width={620}>
      {items.isLoading ? (
        <div className='help'>{t('aging.loading')}</div>
      ) : list.length === 0 ? (
        <div className='empty-state' style={{ padding: 24 }}>
          <div className='t'>{t('aging.drill.empty')}</div>
        </div>
      ) : (
        <table>
          <thead>
            <tr>
              <th>{t('aging.drill.vendor')}</th>
              <th>{t('aging.drill.document')}</th>
              <th>{t('aging.drill.dueDate')}</th>
              <th>{t('aging.drill.daysOverdue')}</th>
              <th style={{ textAlign: 'right' }}>{t('aging.drill.remaining')}</th>
            </tr>
          </thead>
          <tbody>
            {list.map((it) => (
              <tr key={it.installmentId}>
                <td>{it.counterpartyName || '—'}</td>
                <td>{it.documentNumber || '—'}</td>
                <td className='t-mono'>{fmtData(it.dueDate)}</td>
                <td>
                  {it.daysOverdue > 0 ? (
                    <span className='badge-status badge-rejeitado'>
                      {t('aging.drill.daysOverdueValue', { count: it.daysOverdue })}
                    </span>
                  ) : (
                    <span className='badge-status badge-pago'>{t('aging.drill.onTime')}</span>
                  )}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brl(it.remaining)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Drawer>
  );
}

/* ============ Page ============ */
export function AgingPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const [mode, setMode] = useState<PivotMode>('vendor');
  const [drill, setDrill] = useState<PivotRow | null>(null);

  const aging = useAging(empresaId || undefined);
  const costCenters = useCostCenters(empresaId);
  const ccName = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of costCenters.data ?? []) map.set(c.id, `${c.code} — ${c.name}`);
    return map;
  }, [costCenters.data]);

  const report = aging.data;

  const rows = useMemo<PivotRow[]>(() => {
    if (!report) return [];
    if (mode === 'vendor') {
      return report.byVendor.map((r) => ({
        id: r.vendorId,
        name: r.name || '—',
        notYetDue: r.notYetDue,
        overdue1To30: r.overdue1To30,
        overdue31To60: r.overdue31To60,
        overdue61To90: r.overdue61To90,
        overdue90Plus: r.overdue90Plus,
        total: r.total,
        vendorId: r.vendorId,
      }));
    }
    return report.byCostCenter.map((r) => ({
      id: r.costCenterId,
      name: ccName.get(r.costCenterId) ?? r.costCenterId,
      notYetDue: r.notYetDue,
      overdue1To30: r.overdue1To30,
      overdue31To60: r.overdue31To60,
      overdue61To90: r.overdue61To90,
      overdue90Plus: r.overdue90Plus,
      total: r.total,
      costCenterId: r.costCenterId,
    }));
  }, [report, mode, ccName]);

  return (
    <>
      <div className='page-head'>
        <div>
          <h1 className='page-title'>{t('aging.title')}</h1>
          <p className='page-sub'>{t('aging.subtitle')}</p>
        </div>
        <div className='toolbar'>
          <EmpresaPicker />
        </div>
      </div>

      {!empresa ? (
        <EmptyState
          icon={<CalendarClock />}
          title={t('aging.empty.noCompanyTitle')}
          sub={t('aging.empty.noCompanySub')}
        />
      ) : aging.isLoading ? (
        <div className='help'>{t('aging.loading')}</div>
      ) : !report || report.totals.grandTotal === 0 ? (
        <EmptyState icon={<CalendarClock />} title={t('aging.empty.noOpenTitle')} sub={t('aging.empty.noOpenSub')} />
      ) : (
        <>
          <div className='kpi-grid' style={{ gridTemplateColumns: 'repeat(6, 1fr)' }}>
            <Kpi label={t('aging.buckets.notYetDue')} value={brl(report.totals.notYetDue)} />
            <BucketKpi label={t('aging.buckets.overdue1To30')} value={report.totals.overdue1To30} />
            <BucketKpi label={t('aging.buckets.overdue31To60')} value={report.totals.overdue31To60} />
            <BucketKpi label={t('aging.buckets.overdue61To90')} value={report.totals.overdue61To90} />
            <BucketKpi label={t('aging.buckets.overdue90Plus')} value={report.totals.overdue90Plus} />
            <Kpi label={t('aging.grandTotal')} value={brl(report.totals.grandTotal)} />
          </div>

          <Panel
            title={t('aging.title')}
            action={
              <ToolbarField label={t('aging.pivot.label')}>
                <Select value={mode} onChange={(e) => setMode(e.target.value as PivotMode)}>
                  <option value='vendor'>{t('aging.pivot.byVendor')}</option>
                  <option value='costCenter'>{t('aging.pivot.byCostCenter')}</option>
                </Select>
              </ToolbarField>
            }
          >
            <table>
              <thead>
                <tr>
                  <th>{t('aging.columns.name')}</th>
                  <th style={{ textAlign: 'right' }}>{t('aging.columns.notYetDue')}</th>
                  <th style={{ textAlign: 'right' }}>{t('aging.columns.overdue1To30')}</th>
                  <th style={{ textAlign: 'right' }}>{t('aging.columns.overdue31To60')}</th>
                  <th style={{ textAlign: 'right' }}>{t('aging.columns.overdue61To90')}</th>
                  <th style={{ textAlign: 'right' }}>{t('aging.columns.overdue90Plus')}</th>
                  <th style={{ textAlign: 'right' }}>{t('aging.columns.total')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.id} style={{ cursor: 'pointer' }} onClick={() => setDrill(r)}>
                    <td>{r.name}</td>
                    {BUCKET_KEYS.map((k) => (
                      <td key={k} className='amount-cell' style={{ textAlign: 'right' }}>
                        {brl(r[k])}
                      </td>
                    ))}
                    <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                      {brl(r.total)}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td style={{ fontWeight: 600 }}>{t('aging.total')}</td>
                  <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                    {brl(report.totals.notYetDue)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                    {brl(report.totals.overdue1To30)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                    {brl(report.totals.overdue31To60)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                    {brl(report.totals.overdue61To90)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                    {brl(report.totals.overdue90Plus)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                    {brl(report.totals.grandTotal)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </Panel>
        </>
      )}

      {drill && <DrillDrawer companyId={empresaId} row={drill} mode={mode} onClose={() => setDrill(null)} />}
    </>
  );
}

/** Overdue bucket card — warning/alert accent. */
function BucketKpi({ label, value }: { label: string; value: number }) {
  return (
    <div className='kpi-card' style={{ borderColor: 'var(--warning)' }}>
      <div className='kpi-label'>{label}</div>
      <div className='kpi-value' style={{ color: value > 0 ? 'var(--danger)' : undefined }}>
        {brl(value)}
      </div>
    </div>
  );
}
