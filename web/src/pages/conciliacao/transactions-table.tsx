import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Ban, Check, ChevronRight, Link2, Undo2 } from 'lucide-react';
import { type BankTransaction, type ConciliationMismatch, type MatchStatus, type ReconciliationStage } from '../../api';
import { useConfirmMatch, useIgnoreTransaction, useUnmatchTransaction } from '../../queries';
import { brl } from '../../lib';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { onRowClick, Pager, SkeletonRows } from '../../ui';
import { P, usePermissions } from '../../permissions';
import { fmtDate } from '../../datetime';
import { TransactionDetail } from './transaction-detail';

function StatusBadge({ s }: { s: MatchStatus }) {
  const { t } = useTranslation();
  const cls: Record<MatchStatus, string> = {
    Unmatched: 'badge-pendente',
    Suggested: 'badge-aprovado',
    Reconciled: 'badge-pago',
    Ignored: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`conciliacao.status.${s}`)}</span>;
}

/**
 * What the line needs from the operator, in one chip. On the analysis tab every row is Unmatched or Suggested,
 * so repeating that status says nothing — the reason for it is the part that decides what to do next.
 */
function ReasonBadge({ tx }: { tx: BankTransaction }) {
  const { t } = useTranslation();
  // Neutral where there is nothing to reconcile (a tarifa, a credit), warm where a decision is pending, calm
  // where a título is sitting right there waiting to be confirmed.
  const cls: Record<ConciliationMismatch, string> = {
    NoCandidate: 'badge-neutral',
    CreditNotMatched: 'badge-neutral',
    AmountDiffers: 'badge-pendente',
    OutsideDateWindow: 'badge-pendente',
    Ambiguous: 'badge-pendente',
    ExactAvailable: 'badge-aprovado',
  };

  if (tx.matchStatus === 'Suggested' || !tx.mismatch) return <StatusBadge s={tx.matchStatus} />;
  return (
    <span className={`badge-status ${cls[tx.mismatch]}`} title={t(`conciliacao.mismatchHelp.${tx.mismatch}`)}>
      {t(`conciliacao.mismatch.${tx.mismatch}`)}
    </span>
  );
}

/** Per-row actions, gated behind treasury:write. Icon-only with a tooltip: the labels were costing a whole
 * second line per row, and the same four verbs repeat down the entire list. */
function RowActions({ tx, onManual }: { tx: BankTransaction; onManual: (tx: BankTransaction) => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const confirm = useConfirmMatch({ onError: (e) => toast.error(getErrorMessage(e)) });
  const ignore = useIgnoreTransaction({ onError: (e) => toast.error(getErrorMessage(e)) });
  const unmatch = useUnmatchTransaction({ onError: (e) => toast.error(getErrorMessage(e)) });
  const busy = confirm.isPending || ignore.isPending || unmatch.isPending;
  const open = tx.matchStatus === 'Suggested' || tx.matchStatus === 'Unmatched';

  if (!usePermissions().can(P.treasuryWrite)) return null;

  const action = (label: string, icon: React.ReactNode, onClick: () => void, className?: string) => (
    <button
      type='button'
      className={`icon-btn ${className ?? ''}`}
      title={label}
      aria-label={label}
      disabled={busy}
      onClick={onClick}
    >
      {icon}
    </button>
  );

  return (
    <div className='cc-actions'>
      {tx.matchStatus === 'Suggested' &&
        action(t('conciliacao.actions.confirm'), <Check />, () => confirm.mutate({ id: tx.id }), 'cc-ok')}
      {open && action(t('conciliacao.actions.matchManual'), <Link2 />, () => onManual(tx))}
      {open && action(t('conciliacao.actions.ignore'), <Ban />, () => ignore.mutate({ id: tx.id }))}
      {tx.matchStatus === 'Reconciled' &&
        action(t('conciliacao.actions.unmatch'), <Undo2 />, () => unmatch.mutate({ id: tx.id }))}
    </div>
  );
}

const COLS = 6;

function Head({ stage }: { stage: ReconciliationStage }) {
  const { t } = useTranslation();
  return (
    <thead>
      <tr>
        <th className='cc-chevron-col'>
          <span className='sr-only'>{t('conciliacao.detail.expand')}</span>
        </th>
        <th>{t('conciliacao.columns.date')}</th>
        <th>{t('conciliacao.columns.description')}</th>
        <th style={{ textAlign: 'right' }}>{t('conciliacao.columns.amount')}</th>
        <th>{t(stage === 'analysis' ? 'conciliacao.columns.reason' : 'conciliacao.columns.status')}</th>
        <th style={{ textAlign: 'right' }}>{t('conciliacao.columns.actions')}</th>
      </tr>
    </thead>
  );
}

/** Bank-statement lines of the period with per-row reconciliation actions. */
export function TransactionsTable({
  items,
  page,
  totalPages,
  stage,
  onPage,
  onManual,
  loading,
}: {
  items: BankTransaction[];
  page: number;
  totalPages: number;
  /** Which tab is showing. Decides whether the fourth column reads as a reason (still open) or as a status
   * (already treated), and keeps the empty state from telling a user with 138 imported lines to import a file. */
  stage: ReconciliationStage;
  onPage: (page: number) => void;
  onManual: (tx: BankTransaction) => void;
  /** Enquanto o GET carrega: mostra skeleton rows em vez de piscar o estado vazio. */
  loading?: boolean;
}) {
  const { t } = useTranslation();
  // One row open at a time: the point of the rewrite is a list you can scan, and stacked detail panels undo that.
  const [expanded, setExpanded] = useState<string | null>(null);

  if (loading) {
    return (
      <table>
        <Head stage={stage} />
        <tbody>
          <SkeletonRows rows={5} cols={COLS} />
        </tbody>
      </table>
    );
  }

  if (items.length === 0) {
    return (
      <div className='empty-state' style={{ padding: 24 }}>
        <div className='t'>{t('conciliacao.empty.noItemsTitle')}</div>
        <div className='s'>
          {stage === 'resolved' ? t('conciliacao.empty.noReconciledSub') : t('conciliacao.empty.noItemsSub')}
        </div>
      </div>
    );
  }

  return (
    <>
      <table className='cc-table'>
        <Head stage={stage} />
        <tbody>
          {items.map((tx) => {
            const open = expanded === tx.id;
            const toggle = () => setExpanded(open ? null : tx.id);
            return [
              <tr
                key={tx.id}
                className='row-click cc-row'
                tabIndex={0}
                aria-expanded={open}
                aria-label={t(open ? 'conciliacao.detail.collapse' : 'conciliacao.detail.expand')}
                onClick={onRowClick(toggle)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    toggle();
                  }
                }}
              >
                <td className='cc-chevron-col'>
                  <ChevronRight className={`cc-chevron ${open ? 'open' : ''}`} aria-hidden />
                </td>
                <td className='t-mono'>{fmtDate(tx.date)}</td>
                <td className='cc-desc'>{tx.description}</td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brl(tx.amount)}
                </td>
                <td>
                  <ReasonBadge tx={tx} />
                </td>
                {/* A linha alterna no clique; a ação não pode também expandir o que ela está agindo. */}
                <td className='td-action'>
                  <RowActions tx={tx} onManual={onManual} />
                </td>
              </tr>,
              open && (
                <tr key={`${tx.id}-detail`} className='cc-detail-row'>
                  <td colSpan={COLS}>
                    <TransactionDetail tx={tx} onManual={onManual} />
                  </td>
                </tr>
              ),
            ];
          })}
        </tbody>
      </table>
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </>
  );
}
