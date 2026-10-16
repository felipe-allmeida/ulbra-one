import { useTranslation } from 'react-i18next';
import { Link2, Search } from 'lucide-react';
import { type BankTransaction, type ConciliationCandidate } from '../../api';
import { useConfirmMatch, useMatchTransaction } from '../../queries';
import { brl } from '../../lib';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Button } from '../../ui';
import { P, usePermissions } from '../../permissions';
import { fmtDate, fmtDateTime } from '../../datetime';
import { useOriginNav } from '../../nav';

/** One candidate installment: who it is, how far it sits from the line, and a one-click reconcile. */
function CandidateRow({
  candidate: c,
  tx,
  suggested,
}: {
  candidate: ConciliationCandidate;
  tx: BankTransaction;
  /** The matcher already picked this one — confirming it is a different call than matching a near miss. */
  suggested: boolean;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const canWrite = usePermissions().can(P.treasuryWrite);
  const onError = (e: unknown) => toast.error(getErrorMessage(e));
  const confirm = useConfirmMatch({ onError });
  const match = useMatchTransaction({ onError });
  const busy = confirm.isPending || match.isPending;

  return (
    <li className='cc-cand'>
      <div className='cc-cand-main'>
        <span className='cc-cand-name'>{c.counterpartyName || '—'}</span>
        <span className='cc-cand-meta'>
          {t('conciliacao.detail.installmentOf', { sequence: c.sequence, count: c.installmentCount })}
          {c.documentNumber && ` · ${c.documentNumber}`}
          {` · ${t('conciliacao.detail.dueOn', { date: fmtDate(c.dueDate) })}`}
        </span>
      </div>
      <div className='cc-cand-deltas'>
        <span className='amount-cell'>{brl(c.remaining)}</span>
        <span className={`cc-delta ${c.amountDelta === 0 ? 'ok' : 'warn'}`}>
          {c.amountDelta === 0
            ? t('conciliacao.detail.sameAmount')
            : t('conciliacao.detail.amountDelta', { amount: brl(c.amountDelta) })}
        </span>
        <span className={`cc-delta ${c.dayDelta === 0 ? 'ok' : 'warn'}`}>
          {c.dayDelta === 0 ? t('conciliacao.detail.sameDay') : t('conciliacao.detail.dayDelta', { count: c.dayDelta })}
        </span>
      </div>
      {canWrite && (
        <Button
          variant='brand'
          disabled={busy}
          onClick={() =>
            suggested ? confirm.mutate({ id: tx.id }) : match.mutate({ id: tx.id, installmentId: c.installmentId })
          }
        >
          {suggested ? t('conciliacao.actions.confirm') : t('conciliacao.detail.reconcile')}
        </Button>
      )}
    </li>
  );
}

/**
 * The inline body of an expanded statement line: what the bank sent, why the line is still open, and the
 * installments that come closest — each reconcilable without leaving the list.
 */
export function TransactionDetail({ tx, onManual }: { tx: BankTransaction; onManual: (tx: BankTransaction) => void }) {
  const { t } = useTranslation();
  const canWrite = usePermissions().can(P.treasuryWrite);
  const openPayable = useOriginNav();
  const candidates = tx.candidates ?? [];
  const suggested = tx.matchStatus === 'Suggested';
  // A line that was already treated is NOT still looking for a título. The panel used to test only for
  // 'Suggested', so Reconciled and Ignored fell into the "else" and were offered candidates, a "nenhum título
  // candidato" notice and a button to link another parcela — on a line that is already linked.
  const resolved = tx.matchStatus === 'Reconciled' || tx.matchStatus === 'Ignored';
  const matched = tx.matchedTitle ?? null;

  return (
    <div className='cc-detail'>
      <section className='cc-detail-ofx'>
        <h4>{t('conciliacao.detail.ofxTitle')}</h4>
        <dl>
          <dt>{t('conciliacao.detail.fullDescription')}</dt>
          <dd>{tx.description}</dd>
          <dt>{t('conciliacao.detail.direction')}</dt>
          <dd>{t(`conciliacao.detail.${tx.direction}`)}</dd>
          <dt>{t('conciliacao.detail.importedAt')}</dt>
          <dd>{fmtDateTime(tx.createdAt)}</dd>
        </dl>
      </section>

      {resolved ? (
        <section className='cc-detail-cands'>
          <h4>{t('conciliacao.detail.resolvedTitle')}</h4>
          {/* The pairing itself: the payable side against the statement side above. An ignored line has no
              título behind it, which is exactly what ignoring one means. */}
          {matched ? (
            <>
              <dl>
                <dt>{t('conciliacao.detail.matched.vendor')}</dt>
                <dd>{matched.counterpartyName}</dd>
                <dt>{t('conciliacao.detail.matched.installment')}</dt>
                <dd>
                  {t('conciliacao.detail.matched.installmentValue', {
                    sequence: matched.sequence,
                    count: matched.installmentCount,
                    due: fmtDate(matched.dueDate),
                  })}
                </dd>
                <dt>{t('conciliacao.detail.matched.paid')}</dt>
                <dd>
                  {brl(matched.amount)} · {fmtDate(matched.paymentDate)}
                </dd>
                {matched.documentNumber && (
                  <>
                    <dt>{t('conciliacao.detail.matched.document')}</dt>
                    <dd>{matched.documentNumber}</dd>
                  </>
                )}
              </dl>
              <p className='help'>
                {tx.paymentPreexisting
                  ? t('conciliacao.detail.resolvedLinked')
                  : t('conciliacao.detail.resolvedSettled')}
              </p>
              <Button icon={<Link2 />} onClick={() => openPayable(`/app/payables/${matched.payableId}`)}>
                {t('conciliacao.detail.matched.open')}
              </Button>
            </>
          ) : (
            <p className='help'>{t('conciliacao.detail.resolvedIgnored')}</p>
          )}
        </section>
      ) : (
        <section className='cc-detail-cands'>
          <h4>{suggested ? t('conciliacao.detail.suggestedTitle') : t('conciliacao.detail.candidatesTitle')}</h4>
          {/* The reason comes first: on a NoCandidate line it is the whole answer, and there is no list below it. */}
          {tx.mismatch && <p className='help'>{t(`conciliacao.mismatchHelp.${tx.mismatch}`)}</p>}
          {candidates.length > 0 ? (
            <ul className='cc-cands'>
              {candidates.map((c) => (
                <CandidateRow key={c.installmentId} candidate={c} tx={tx} suggested={suggested} />
              ))}
            </ul>
          ) : (
            <p className='help'>{t('conciliacao.detail.noCandidates')}</p>
          )}
          {canWrite && (
            <Button icon={candidates.length > 0 ? <Search /> : <Link2 />} onClick={() => onManual(tx)}>
              {t('conciliacao.detail.searchOther')}
            </Button>
          )}
        </section>
      )}
    </div>
  );
}
