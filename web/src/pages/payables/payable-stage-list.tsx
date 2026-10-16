import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { FileText, Wallet } from 'lucide-react';
import { type PayableStageItem } from '../../api';
import { useRequestFiscalDocument } from '../../queries';
import { brl } from '../../lib';
import { Button, EmptyState, Pager, Skeleton } from '../../ui';
import { useToast } from '../../toast';
import { fmtDate } from '../../datetime';
import { ExpandableRow } from '../requisitions/expandable-row';

/** One label→value row of a `.detail-dl` list. Local since the shared requisitions dl-row was folded into
 * the requisition summary card (PR #139); the Contábil detail grids still need a plain row. */
export function DlRow({ label, value }: { label: string; value: string }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </>
  );
}

interface Flag {
  text: string;
  tone: 'ok' | 'warn' | 'bad' | 'calm';
}

/** Human number for a payable card: the origin requisition's friendly code (#42) when present, else a short
 * id fallback — a payable has no number of its own (AEL-865). */
function payableNumber(item: PayableStageItem): string {
  if (item.originRequisitionCode != null) return `#${item.originRequisitionCode}`;
  return `#${item.payableId.replace(/^payable_/, '').slice(0, 6)}`;
}

/**
 * Compact expandable list for the Contábil home's stage tabs (mock "view_cont_lanc"): fornecedor +
 * natureza + gross value per row, expanding to the classification-relevant detail + action. Replaces the
 * wide payable table (too many columns to fit) — the net/final value is intentionally NOT shown here; it
 * only matters downstream in Finance. Per-tab specifics come from `flagFor` (the at-a-glance chip) and
 * `renderBody` (the expanded content + action button).
 */
export function PayableStageList({
  items,
  page,
  totalPages,
  onPage,
  emptyTitle,
  emptySub,
  flagFor,
  companyNameFor,
  renderBody,
  onEnter,
  metaExtra,
  loading,
}: {
  items: PayableStageItem[];
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
  emptyTitle: string;
  emptySub?: string;
  flagFor?: (item: PayableStageItem) => Flag | undefined;
  /** Consolidated "Todas" (AEL-906): the item's company, prefixed onto the meta line to tell companies apart. */
  companyNameFor?: (item: PayableStageItem) => string | undefined;
  /** Expanded body. Not needed when `onEnter` is set — an entering row never expands. */
  renderBody?: (item: PayableStageItem) => ReactNode;
  /** When given, clicking a row enters the item instead of expanding it (the app-wide list pattern). */
  onEnter?: (item: PayableStageItem) => void;
  /** Extra detail appended to the row's meta line — for rows that enter on click, whatever used to live in the
   *  expanded body has to be visible here or it is simply lost. */
  metaExtra?: (item: PayableStageItem) => string | undefined;
  /** Enquanto o GET carrega: mostra cards skeleton em vez de piscar o estado vazio. */
  loading?: boolean;
}) {
  if (loading) {
    return (
      <div className='rq-list'>
        {Array.from({ length: 4 }).map((_, i) => (
          <div className='xrow' key={i}>
            <div className='xrow-head' style={{ cursor: 'default' }}>
              <span className='rq-row-main xrow-main' style={{ gap: 6 }}>
                <Skeleton width={150} />
                <Skeleton width={230} height={10} />
              </span>
              <span className='rq-row-right' style={{ marginLeft: 'auto' }}>
                <Skeleton width={90} />
              </span>
            </div>
          </div>
        ))}
      </div>
    );
  }
  if (items.length === 0) {
    return <EmptyState icon={<Wallet />} title={emptyTitle} sub={emptySub} />;
  }
  return (
    <>
      <div className='rq-list'>
        {items.map((item) => {
          const company = companyNameFor?.(item);
          const extra = metaExtra?.(item);
          const metaLine = [company, item.description, extra].filter(Boolean).join(' · ');
          return (
            <ExpandableRow
              key={item.payableId}
              number={payableNumber(item)}
              title={item.counterpartyName || '—'}
              meta={metaLine}
              value={brl(item.totalAmount)}
              flag={flagFor?.(item)}
              onEnter={onEnter ? () => onEnter(item) : undefined}
            >
              {renderBody?.(item)}
            </ExpandableRow>
          );
        })}
      </div>
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </>
  );
}

/** "Solicitar NF": asks the requester (via notification) to provide a payable's Nota Fiscal. Shown on
 * NF-pending, requisition-sourced cards; the payable stays pending until the NF propagates back. */
export function RequestFiscalDocumentButton({ payableId }: { payableId: string }) {
  const { t } = useTranslation();
  const toast = useToast();
  const request = useRequestFiscalDocument();
  return (
    <Button
      icon={<FileText />}
      disabled={request.isPending}
      onClick={() =>
        request.mutate(
          { id: payableId },
          { onSuccess: () => toast.success(t('accounting.review.requestFiscalDocument.done')) },
        )
      }
    >
      {t('accounting.review.requestFiscalDocument.button')}
    </Button>
  );
}

/** Shared expanded-body detail for a stage item (the columns the wide table used to show): due date,
 * cost center and installment count — never the net/final value (Finance's concern, not accounting's). */
export function PayableStageDetail({ item, centroName }: { item: PayableStageItem; centroName: string }) {
  const { t } = useTranslation();
  return (
    <dl className='detail-dl'>
      <DlRow label={t('payables.columns.dueDate')} value={fmtDate(item.firstDueDate)} />
      <DlRow label={t('payables.columns.costCenter')} value={centroName} />
      <DlRow label={t('payables.columns.installments')} value={String(item.installmentCount)} />
    </dl>
  );
}
