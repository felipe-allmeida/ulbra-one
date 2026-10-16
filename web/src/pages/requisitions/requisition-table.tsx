import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ChevronDown, ChevronsUpDown, ChevronUp, ClipboardList } from 'lucide-react';
import { type Requisition } from '../../api';
import { useApp, ALL_COMPANIES } from '../../store';
import { useCostCenters, useUnits } from '../../queries';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { EmptyState, onRowClick, Pager, Panel, SkeletonRows } from '../../ui';
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

  // Cabeçalho ordenável: clique alterna asc/desc do campo; um chevron indica a direção corrente.
  const Th = ({ field, children, right }: { field: string; children: string; right?: boolean }) => {
    const style = right ? { textAlign: 'right' as const } : undefined;
    if (!onSort) return <th style={style}>{children}</th>;
    const dir = sort === field ? 'asc' : sort === `-${field}` ? 'desc' : null;
    return (
      <th style={style}>
        <button type='button' className='th-sort' onClick={() => onSort(field)}>
          <span>{children}</span>
          {dir === 'asc' ? (
            <ChevronUp size={13} />
          ) : dir === 'desc' ? (
            <ChevronDown size={13} />
          ) : (
            <ChevronsUpDown size={13} className='th-sort-idle' />
          )}
        </button>
      </th>
    );
  };

  return (
    <Panel title={`${title} · ${t('requisitions.count', { count: total })}`}>
      <table>
        <thead>
          <tr>
            <Th field='code'>{t('requisitions.columns.code')}</Th>
            {isAll && <th>{t('requisitions.columns.company')}</th>}
            <th>{t('requisitions.columns.unit')}</th>
            <th>{t('requisitions.columns.costCenter')}</th>
            <th>{t('requisitions.columns.vendor')}</th>
            <th>{t('requisitions.columns.type')}</th>
            <Th field='competency'>{t('requisitions.columns.competency')}</Th>
            <Th field='estimate' right>
              {t('requisitions.columns.estimate')}
            </Th>
            <Th field='status'>{t('requisitions.columns.status')}</Th>
            {extraColumn && <th>{extraColumn.header}</th>}
          </tr>
        </thead>
        <tbody>
          {items.map((r) => (
            <tr key={r.id} className='row-click' onClick={onRowClick(() => openDetail(`/app/requisitions/${r.id}`))}>
              <td className='t-mono'>#{r.code}</td>
              {isAll && <td>{companyName(r.companyId)}</td>}
              <td>{unitName(r.unitId)}</td>
              <td>{centroName(r.costCenterId)}</td>
              <td>{r.vendorName || '—'}</td>
              <td>{t(`requisitions.type.${r.type}`)}</td>
              <td className='t-mono'>{competencyWindowText(r, t('requisitions.competencyWindow.recurring'))}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(r.estimateValue)}
              </td>
              <td>
                <span style={{ display: 'inline-flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
                  <RequisitionStatusBadge s={r.status} />
                  <RequisitionSlaChip req={r} />
                </span>
              </td>
              {extraColumn && <td>{extraColumn.cell(r)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
