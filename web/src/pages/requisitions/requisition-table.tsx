import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ChevronDown, ChevronsUpDown, ChevronUp, ClipboardList } from 'lucide-react';
import { type Requisition } from '../../api';
import { useApp, ALL_COMPANIES } from '../../store';
import { useCostCenters, useUnits } from '../../queries';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { EmptyState, Pager, Panel, SkeletonRows } from '../../ui';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { RequisitionSlaChip, RequisitionStatusBadge } from './badges';
import { competencyWindowText } from './competency';

/** Tabela única de requisições (mesma ordem de colunas em todas as telas). Cabeçalhos ordenáveis
 * quando `onSort` é fornecido; `sort` (`campo`/`-campo`) reflete a direção corrente. */
export function RequisitionTable({
  companyId,
  title,
  items,
  total,
  page,
  totalPages,
  onPage,
  sort,
  onSort,
  extraColumn,
  loading,
}: {
  companyId: string;
  title: string;
  items: Requisition[];
  total: number;
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
  sort?: string;
  onSort?: (field: string) => void;
  extraColumn?: { header: string; cell: (r: Requisition) => ReactNode };
  /** Enquanto o GET carrega: mostra skeleton rows em vez de piscar o estado vazio. */
  loading?: boolean;
}) {
  const { t } = useTranslation();
  const openDetail = useOriginNav();
  const { empresas } = useApp();
  // Consolidated "Todas as empresas" (AEL-906): show which company each requisition belongs to.
  const isAll = companyId === ALL_COMPANIES;
  const units = useUnits(companyId).data ?? [];
  const centros = useCostCenters(companyId).data ?? [];
  const unitName = (id: string) => units.find((u) => u.id === id)?.acronym ?? '—';
  const centroName = (id: string) => centros.find((c) => c.id === id)?.name ?? '—';
  const companyName = (id: string) => empresas.find((e) => e.id === id)?.legalName ?? '—';

  if (loading) {
    const cols = 8 + (isAll ? 1 : 0) + (extraColumn ? 1 : 0);
    return (
      <Panel title={title}>
        <table>
          <thead>
            <tr>
              <th>{t('requisitions.columns.code')}</th>
              {isAll && <th>{t('requisitions.columns.company')}</th>}
              <th>{t('requisitions.columns.unit')}</th>
              <th>{t('requisitions.columns.costCenter')}</th>
              <th>{t('requisitions.columns.vendor')}</th>
              <th>{t('requisitions.columns.type')}</th>
              <th>{t('requisitions.columns.competency')}</th>
              <th style={{ textAlign: 'right' }}>{t('requisitions.columns.estimate')}</th>
              <th>{t('requisitions.columns.status')}</th>
              {extraColumn && <th>{extraColumn.header}</th>}
            </tr>
          </thead>
          <tbody>
            <SkeletonRows rows={5} cols={cols} />
          </tbody>
        </table>
      </Panel>
    );
  }

  if (items.length === 0) {
    return <EmptyState icon={<ClipboardList />} title={`${title} — ${t('requisitions.empty.noItems')}`} />;
  }

  // Cabeçalho ordenável: clique alterna asc/desc do campo; um chevron indica a direção corrente. Vira o
  // `header` da coluna (ReactNode — ver ResponsiveTable.tsx); no card do telefone essa área nunca aparece
  // como cabeçalho (só como rótulo de uma coluna `detail`), então o botão de ordenar só existe no desktop.
  const sortHeader = (field: string, label: string) => {
    if (!onSort) return label;
    const dir = sort === field ? 'asc' : sort === `-${field}` ? 'desc' : null;
    return (
      <button type='button' className='th-sort' onClick={() => onSort(field)}>
        <span>{label}</span>
        {dir === 'asc' ? (
          <ChevronUp size={13} />
        ) : dir === 'desc' ? (
          <ChevronDown size={13} />
        ) : (
          <ChevronsUpDown size={13} className='th-sort-idle' />
        )}
      </button>
    );
  };

  // Ordem = a ordem já renderizada hoje. código é a única `title` (identificador curto e sempre presente,
  // ver o mesmo papel em vendor-history.tsx); unidade/fornecedor são os `meta` mais úteis pra reconhecer a
  // linha de relance (empresa/natureza/tipo/competência viram `detail` — não cabem os 6 no card, ver aviso
  // de cardinalidade em ResponsiveTable.tsx). estimativa é o único `amount`; status carrega os dois badges
  // (selo + chip de SLA), papel `status` empilha os dois. `extraColumn` (ação de triagem/aprovação) já tem
  // cabeçalho próprio no desktop de hoje — não é uma coluna de ação "muda", então mantém `header` normal.
  const columns: Column<Requisition>[] = [
    {
      key: 'code',
      header: sortHeader('code', t('requisitions.columns.code')),
      role: 'title',
      cell: (r) => <span className='t-mono'>#{r.code}</span>,
    },
    ...(isAll
      ? [
          {
            key: 'company',
            header: t('requisitions.columns.company'),
            role: 'detail' as const,
            cell: (r: Requisition) => companyName(r.companyId),
          },
        ]
      : []),
    { key: 'unit', header: t('requisitions.columns.unit'), role: 'meta', cell: (r) => unitName(r.unitId) },
    {
      key: 'costCenter',
      header: t('requisitions.columns.costCenter'),
      role: 'detail',
      cell: (r) => centroName(r.costCenterId),
    },
    { key: 'vendor', header: t('requisitions.columns.vendor'), role: 'meta', cell: (r) => r.vendorName || '—' },
    {
      key: 'type',
      header: t('requisitions.columns.type'),
      role: 'detail',
      cell: (r) => t(`requisitions.type.${r.type}`),
    },
    {
      key: 'competency',
      header: sortHeader('competency', t('requisitions.columns.competency')),
      role: 'detail',
      cell: (r) => (
        <span className='t-mono'>{competencyWindowText(r, t('requisitions.competencyWindow.recurring'))}</span>
      ),
    },
    {
      key: 'estimate',
      header: sortHeader('estimate', t('requisitions.columns.estimate')),
      role: 'amount',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brl(r.estimateValue)}</span>,
    },
    {
      key: 'status',
      header: sortHeader('status', t('requisitions.columns.status')),
      role: 'status',
      cell: (r) => (
        <span style={{ display: 'inline-flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
          <RequisitionStatusBadge s={r.status} />
          <RequisitionSlaChip req={r} />
        </span>
      ),
    },
    ...(extraColumn
      ? [
          {
            key: 'extra',
            header: extraColumn.header,
            role: 'detail' as const,
            cell: (r: Requisition) => extraColumn.cell(r),
          },
        ]
      : []),
  ];

  return (
    <Panel title={`${title} · ${t('requisitions.count', { count: total })}`}>
      <ResponsiveTable
        rows={items}
        columns={columns}
        rowKey={(r) => r.id}
        onRowClick={(r) => openDetail(`/app/requisitions/${r.id}`)}
      />
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
