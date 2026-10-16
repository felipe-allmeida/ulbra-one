import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Wallet } from 'lucide-react';
import { type PayableStageItem, type PayableStatus } from '../../api';
import { brl } from '../../lib';
import { useOriginNav } from '../../nav';
import { EmptyState, Pager, Panel, SkeletonRows } from '../../ui';
import { fmtDate } from '../../datetime';
import { SlaChips } from '../../components/workflow';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { useNames } from './use-names';

// The net only becomes final when accounting completes (withholdings lock at Scheduled).
const isNetLocked = (s: PayableStatus) => s === 'Scheduled' || s === 'Paid';

// "Nota Fiscal pendente": no NF on PendingAccounting/Scheduled/Paid (Advance exempt) — mirrors the backend
// fiscalDocumentPending bucket. The auto-schedule (AEL-846) pays without NF until one is attached.
const isFiscalDocPending = (item: PayableStageItem) =>
  item.hasFiscalDocument === false &&
  item.origin !== 'Advance' &&
  (item.status === 'PendingAccounting' || item.status === 'Scheduled' || item.status === 'Paid');

export function PayableTable({
  companyId,
  stageLabel,
  items,
  total,
  page,
  totalPages,
  onPage,
  extraColumn,
  statusCell,
  showNet,
  loading,
}: {
  companyId: string;
  stageLabel: string;
  items: PayableStageItem[];
  total: number;
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
  extraColumn?: { header: string; cell: (item: PayableStageItem) => ReactNode };
  /** Overrides the status column content (default: stage SLA chips + pendency tags). */
  statusCell?: (item: PayableStageItem) => ReactNode;
  /** Post-accounting tables: renders "Valor bruto" + "Valor líquido" columns; the net cell shows
   * '—' until the withholdings lock (status Scheduled/Paid). */
  showNet?: boolean;
  /** Enquanto o GET carrega: mostra skeleton rows em vez de piscar o estado vazio. */
  loading?: boolean;
}) {
  const { t } = useTranslation();
  const openDetail = useOriginNav();
  const { centroNome } = useNames(companyId);

  // Colunas fixas (7) + líquido + coluna extra — mantém o skeleton alinhado ao cabeçalho.
  const colCount = 7 + (showNet ? 1 : 0) + (extraColumn ? 1 : 0);

  const head = (
    <thead>
      <tr>
        <th>{t('payables.columns.dueDate')}</th>
        <th>{t('payables.columns.description')}</th>
        <th>{t('payables.columns.vendor')}</th>
        <th>{t('payables.columns.costCenter')}</th>
        <th style={{ textAlign: 'center' }}>{t('payables.columns.installments')}</th>
        <th style={{ textAlign: 'right' }}>
          {t(showNet ? 'payables.columns.grossAmount' : 'payables.columns.amount')}
        </th>
        {showNet && <th style={{ textAlign: 'right' }}>{t('payables.columns.netAmount')}</th>}
        <th>{t('payables.columns.status')}</th>
        {extraColumn && <th>{extraColumn.header}</th>}
      </tr>
    </thead>
  );

  if (loading) {
    return (
      <Panel title={stageLabel}>
        <table>
          {head}
          <tbody>
            <SkeletonRows rows={5} cols={colCount} />
          </tbody>
        </table>
      </Panel>
    );
  }

  if (items.length === 0) {
    // No create CTA here: these tables list payables by stage (Contábil review, advances, pendências…), where
    // creating a payable isn't the action; the payables page header already offers "Nova conta" where it belongs.
    return <EmptyState icon={<Wallet />} title={`${stageLabel} — ${t('payables.empty.noItemsTitle')}`} />;
  }

  // Conteúdo padrão da coluna de status: chips de SLA + as tags de adiantamento e NF pendente.
  // Extraído do <td> inline para poder ser passado como `cell` de uma coluna.
  const defaultStatus = (l: PayableStageItem) => (
    <>
      <SlaChips stageLabel={stageLabel} dueDate={l.firstDueDate} />
      {l.origin === 'Advance' && (
        <span className='badge-status badge-remessa' style={{ marginLeft: 6 }}>
          {t('payables.tags.advance')}
        </span>
      )}
      {isFiscalDocPending(l) && (
        <span className='badge-status badge-pendente' style={{ marginLeft: 6 }}>
          {t('payables.tags.fiscalDocPending')}
        </span>
      )}
    </>
  );

  // Ordem = a ordem já renderizada hoje (vencimento antes de descrição) — preserva o desktop pixel a pixel.
  // Papel não depende de posição: description é a única `title`, fornecedor/vencimento são `meta` (contexto de
  // relance), bruto é o único `amount` (o número que o olho procura); líquido é um segundo valor monetário e por
  // isso vai para `detail`, não para um segundo `amount` — ver o aviso de cardinalidade em ResponsiveTable.tsx.
  const columns: Column<PayableStageItem>[] = [
    {
      key: 'dueDate',
      header: t('payables.columns.dueDate'),
      role: 'meta',
      cell: (l) => <span className='t-mono'>{fmtDate(l.firstDueDate)}</span>,
    },
    { key: 'description', header: t('payables.columns.description'), role: 'title', cell: (l) => l.description },
    { key: 'vendor', header: t('payables.columns.vendor'), role: 'meta', cell: (l) => l.counterpartyName || '—' },
    {
      key: 'costCenter',
      header: t('payables.columns.costCenter'),
      role: 'detail',
      cell: (l) => centroNome[l.costCenterId] ?? '—',
    },
    {
      key: 'installments',
      header: t('payables.columns.installments'),
      role: 'detail',
      align: 'center',
      cell: (l) => l.installmentCount,
    },
    {
      key: 'total',
      header: t(showNet ? 'payables.columns.grossAmount' : 'payables.columns.amount'),
      role: 'amount',
      align: 'right',
      cell: (l) => <span className='amount-cell'>{brl(l.totalAmount)}</span>,
    },
    ...(showNet
      ? [
          {
            key: 'net',
            header: t('payables.columns.netAmount'),
            role: 'detail' as const,
            align: 'right' as const,
            cell: (l: PayableStageItem) => (
              <span className='amount-cell'>{isNetLocked(l.status) ? brl(l.netTotal) : '—'}</span>
            ),
          },
        ]
      : []),
    {
      key: 'status',
      header: t('payables.columns.status'),
      role: 'status',
      // stopPropagation contra o onRowClick é automático para role 'status' (ResponsiveTable.tsx) — um
      // clique num chip não abre o detalhe, em ambos os tamanhos.
      cell: (l) => statusCell?.(l) ?? defaultStatus(l),
    },
    ...(extraColumn
      ? [
          {
            key: 'extra',
            header: extraColumn.header,
            role: 'detail' as const,
            // A coluna extra carrega botões de ação (aprovar/rejeitar/classificar): REVIEW_MANUALLY
            // primitivo o mesmo stopPropagation do status, para eles não também navegarem a linha.
            cell: (l: PayableStageItem) => extraColumn.cell(l),
          },
        ]
      : []),
  ];

  return (
    <Panel title={`${stageLabel} · ${t('payables.count', { count: total })}`}>
      <ResponsiveTable
        rows={items}
        columns={columns}
        rowKey={(l) => l.payableId}
        onRowClick={(l) => openDetail(`/app/payables/${l.payableId}`)}
      />
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
