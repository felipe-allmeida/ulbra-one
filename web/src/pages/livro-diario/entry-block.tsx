import { useTranslation } from 'react-i18next';
import { type JournalBookEntryView } from '../../api';
import { brlCents } from '../../lib';

/** One scriptured entry (lançamento) rendered as a mini débito/crédito table with its lines expanded.
 *  Read-only — the Livro Diário never edits. Reversed entries are flagged with a badge. */
export function EntryBlock({ entry }: { entry: JournalBookEntryView }) {
  const { t } = useTranslation();
  const reversed = entry.status === 'Reversed';
  return (
    <div className='journal-entry-block' style={{ marginBottom: 16 }}>
      <div style={{ marginBottom: 4 }}>
        <strong>
          {t('accounting.journalBook.entry')} {entry.number != null ? `#${entry.number}` : ''}
        </strong>{' '}
        — {entry.description}
        {reversed && (
          <span className='badge-status badge-rejeitado' style={{ marginLeft: 8 }}>
            {t('accounting.journalBook.reversed')}
          </span>
        )}
      </div>
      <table className='t-mono'>
        <thead>
          <tr>
            <th>{t('accounting.journalBook.columns.account')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.journalBook.columns.debit')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.journalBook.columns.credit')}</th>
          </tr>
        </thead>
        <tbody>
          {entry.lines.map((l, i) => (
            <tr key={i}>
              <td>
                {l.accountCode} · {l.accountName}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {l.side === 'Debit' ? brlCents(l.amount) : ''}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {l.side === 'Credit' ? brlCents(l.amount) : ''}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
