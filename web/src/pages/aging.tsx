import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { CalendarClock } from 'lucide-react';
import { useApp } from '../store';
import { useAging, useAgingItems, useCostCenters } from '../queries';
import { brl } from '../lib';
import { Drawer, EmptyState, Kpi, Panel, Select, ToolbarField, LoadingState } from '../ui';
import { fmtDate } from '../datetime';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';
import type { AgingItem, AgingReport } from '../api';

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
        <LoadingState />
      ) : list.length === 0 ? (
        <div className='empty-state' style={{ padding: 24 }}>
          <div className='t'>{t('aging.drill.empty')}</div>
        </div>
      ) : (
        <div className='drawer-table-scroll'>
          <AgingDrillTable items={list} />
        </div>
      )}
    </Drawer>
  );
}

/**
 * Ordem = vendor/document/dueDate/daysOverdue/remaining, igual a hoje. `vendor` é o `title`;
 * `document` e `dueDate` são os dois `meta`; `daysOverdue` (badge) é `status`; `remaining` é o
 * único `amount`. Sem `onRowClick` — a linha nunca navegou.
 */
function AgingDrillTable({ items }: { items: AgingItem[] }) {
  const { t } = useTranslation();
  const columns: Column<AgingItem>[] = [
    { key: 'vendor', header: t('aging.drill.vendor'), role: 'title', cell: (it) => it.counterpartyName || '—' },
    { key: 'document', header: t('aging.drill.document'), role: 'meta', cell: (it) => it.documentNumber || '—' },
    {
      key: 'dueDate',
      header: t('aging.drill.dueDate'),
      role: 'meta',
      cell: (it) => <span className='t-mono'>{fmtDate(it.dueDate)}</span>,
    },
    {
      key: 'daysOverdue',
      header: t('aging.drill.daysOverdue'),
      role: 'status',
      cell: (it) =>
        it.daysOverdue > 0 ? (
          <span className='badge-status badge-rejeitado'>
            {t('aging.drill.daysOverdueValue', { count: it.daysOverdue })}
          </span>
        ) : (
          <span className='badge-status badge-pago'>{t('aging.drill.onTime')}</span>
        ),
    },
    {
      key: 'remaining',
      header: t('aging.drill.remaining'),
      role: 'amount',
      align: 'right',
      cell: (it) => <span className='amount-cell'>{brl(it.remaining)}</span>,
    },
  ];

  return <ResponsiveTable rows={items} columns={columns} rowKey={(it) => it.installmentId} />;
}

/* ============ Page ============ */
export function AgingPage() {
  const { t } = useTranslation();
  const { empresaId, empresas, isAllCompanies, scopeReady } = useApp();
  const [mode, setMode] = useState<PivotMode>('vendor');
  const [drill, setDrill] = useState<PivotRow | null>(null);

  const aging = useAging(empresaId || undefined);
  const costCenters = useCostCenters(empresaId);
  // Consolidated "Todas" (AEL-906): keep the company alongside each cost center to disambiguate same-named ones.
  const ccInfo = useMemo(() => {
    const map = new Map<string, { label: string; companyId: string }>();
    for (const c of costCenters.data ?? []) map.set(c.id, { label: `${c.code} — ${c.name}`, companyId: c.companyId });
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
    const nomeEmpresa = (id: string) => empresas.find((e) => e.id === id)?.legalName ?? '—';
    return report.byCostCenter.map((r) => {
      const info = ccInfo.get(r.costCenterId);
      const label = info?.label ?? r.costCenterId;
      return {
        id: r.costCenterId,
        name: isAllCompanies && info ? `${label} · ${nomeEmpresa(info.companyId)}` : label,
        notYetDue: r.notYetDue,
        overdue1To30: r.overdue1To30,
        overdue31To60: r.overdue31To60,
        overdue61To90: r.overdue61To90,
        overdue90Plus: r.overdue90Plus,
        total: r.total,
        costCenterId: r.costCenterId,
      };
    });
  }, [report, mode, ccInfo, isAllCompanies, empresas]);

  return (
    <>
      <div className='page-head'>
        <div>
          <h1 className='page-title'>{t('aging.title')}</h1>
          <p className='page-sub'>{t('aging.subtitle')}</p>
        </div>
        <div className='toolbar'></div>
      </div>

      {!scopeReady ? (
        <EmptyState
          icon={<CalendarClock />}
          title={t('aging.empty.noCompanyTitle')}
          sub={t('aging.empty.noCompanySub')}
        />
      ) : aging.isLoading ? (
        <LoadingState />
      ) : !report || report.totals.grandTotal === 0 ? (
        <EmptyState icon={<CalendarClock />} title={t('aging.empty.noOpenTitle')} sub={t('aging.empty.noOpenSub')} />
      ) : (
        <>
          <div className='kpi-grid'>
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
            <AgingPivotTable rows={rows} totals={report.totals} onDrill={setDrill} />
          </Panel>
        </>
      )}

      {drill && <DrillDrawer companyId={empresaId} row={drill} mode={mode} onClose={() => setDrill(null)} />}
    </>
  );
}

/**
 * Ordem = name/notYetDue/overdue1To30/overdue31To60/overdue61To90/overdue90Plus/total, igual a
 * hoje. `name` (fornecedor ou centro de custo) é o `title`; seis valores monetários — só um pode
 * ser `amount` (ver aviso de cardinalidade). `total` já era o único em negrito no desktop original,
 * então é o natural `amount`; os cinco buckets vão para `detail`. onRowClick preserva o clique que
 * abre o drill drawer. Rodapé: os `footCell` leem `report.totals.*` (a agregação da API), nunca
 * somam `rows` — `rows` aqui já é a lista inteira (sem paginação), mas o valor de origem é mantido
 * de propósito para não depender de recomputar o mesmo total por dois caminhos diferentes.
 */
function AgingPivotTable({
  rows,
  totals,
  onDrill,
}: {
  rows: PivotRow[];
  totals: AgingReport['totals'];
  onDrill: (row: PivotRow) => void;
}) {
  const { t } = useTranslation();
  const bucketCol = (key: (typeof BUCKET_KEYS)[number], header: string): Column<PivotRow> => ({
    key,
    header,
    role: 'detail',
    align: 'right',
    cell: (r) => <span className='amount-cell'>{brl(r[key])}</span>,
    footCell: () => (
      <span className='amount-cell' style={{ fontWeight: 600 }}>
        {brl(totals[key])}
      </span>
    ),
  });

  const columns: Column<PivotRow>[] = [
    { key: 'name', header: t('aging.columns.name'), role: 'title', cell: (r) => r.name },
    ...BUCKET_KEYS.map((key) => bucketCol(key, t(`aging.columns.${key}`))),
    {
      key: 'total',
      header: t('aging.columns.total'),
      role: 'amount',
      align: 'right',
      cell: (r) => (
        <span className='amount-cell' style={{ fontWeight: 600 }}>
          {brl(r.total)}
        </span>
      ),
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {brl(totals.grandTotal)}
        </span>
      ),
    },
  ];

  return (
    <ResponsiveTable
      rows={rows}
      columns={columns}
      rowKey={(r) => r.id}
      onRowClick={onDrill}
      footLabel={t('aging.total')}
    />
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
