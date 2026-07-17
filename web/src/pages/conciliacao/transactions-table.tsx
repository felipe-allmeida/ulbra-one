import { useTranslation } from 'react-i18next';
import { Ban, Check, Link2, Undo2 } from 'lucide-react';
import { type BankTransaction, type MatchStatus } from '../../api';
import { useConfirmMatch, useIgnoreTransaction, useUnmatchTransaction } from '../../queries';
import { brl } from '../../lib';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Button, Pager } from '../../ui';
import { P, usePermissions } from '../../permissions';
import { fmtDate } from '../../datetime';

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

/** Per-row actions, gated behind treasury:write. */
function RowActions({ tx, onManual }: { tx: BankTransaction; onManual: (tx: BankTransaction) => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const confirm = useConfirmMatch({ onError: (e) => toast.error(getErrorMessage(e)) });
  const ignore = useIgnoreTransaction({ onError: (e) => toast.error(getErrorMessage(e)) });
  const unmatch = useUnmatchTransaction({ onError: (e) => toast.error(getErrorMessage(e)) });
  const busy = confirm.isPending || ignore.isPending || unmatch.isPending;

  if (!usePermissions().can(P.treasuryWrite)) return null;

  return (
    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
      {tx.matchStatus === 'Suggested' && (
        <Button className='btn-approve' icon={<Check />} disabled={busy} onClick={() => confirm.mutate({ id: tx.id })}>
          {t('conciliacao.actions.confirm')}
        </Button>
      )}
      {(tx.matchStatus === 'Suggested' || tx.matchStatus === 'Unmatched') && (
        <Button icon={<Link2 />} disabled={busy} onClick={() => onManual(tx)}>
          {t('conciliacao.actions.matchManual')}
        </Button>
      )}
      {(tx.matchStatus === 'Suggested' || tx.matchStatus === 'Unmatched') && (
        <Button icon={<Ban />} disabled={busy} onClick={() => ignore.mutate({ id: tx.id })}>
          {t('conciliacao.actions.ignore')}
        </Button>
      )}
      {tx.matchStatus === 'Reconciled' && (
        <Button icon={<Undo2 />} disabled={busy} onClick={() => unmatch.mutate({ id: tx.id })}>
          {t('conciliacao.actions.unmatch')}
        </Button>
      )}
    </div>
  );
}

/** Bank-statement lines of the period with per-row reconciliation actions. */
export function TransactionsTable({
  items,
  page,
  totalPages,
  onPage,
  onManual,
}: {
  items: BankTransaction[];
  page: number;
  totalPages: number;
  onPage: (page: number) => void;
  onManual: (tx: BankTransaction) => void;
}) {
  const { t } = useTranslation();

  if (items.length === 0) {
    return (
      <div className='empty-state' style={{ padding: 24 }}>
        <div className='t'>{t('conciliacao.empty.noItemsTitle')}</div>
        <div className='s'>{t('conciliacao.empty.noItemsSub')}</div>
      </div>
    );
  }

  return (
    <>
      <table>
        <thead>
          <tr>
            <th>{t('conciliacao.columns.date')}</th>
            <th>{t('conciliacao.columns.description')}</th>
            <th style={{ textAlign: 'right' }}>{t('conciliacao.columns.amount')}</th>
            <th>{t('conciliacao.columns.status')}</th>
            <th style={{ textAlign: 'right' }}>{t('conciliacao.columns.actions')}</th>
          </tr>
        </thead>
        <tbody>
          {items.map((tx) => (
            <tr key={tx.id}>
              <td className='t-mono'>{fmtDate(tx.date)}</td>
              <td>
                {tx.description}
                {tx.matchStatus === 'Suggested' && <div className='help'>{t('conciliacao.suggested')}</div>}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(tx.amount)}
              </td>
              <td>
                <StatusBadge s={tx.matchStatus} />
              </td>
              <td className='td-action'>
                <RowActions tx={tx} onManual={onManual} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </>
  );
}
