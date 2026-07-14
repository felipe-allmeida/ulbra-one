import { useTranslation } from 'react-i18next';
import { type JournalBookEntryView } from '../../api';
import { brlCents } from '../../lib';

/** One scriptured entry (lançamento) as a <tbody> group inside the day's table (see DayGroup): a spanning
 *  header row (number + description, reversed badge) followed by its débito/crédito lines. Read-only. */
export function EntryBlock({ entry }: { entry: JournalBookEntryView }) {
  const { t } = useTranslation();
  const reversed = entry.status === 'Reversed';
  return (
    <tbody>
      <tr>
        <td colSpan={3} className='journal-entry-head'>
          <strong>
            {t('accounting.journalBook.entry')} {entry.number != null ? `#${entry.number}` : ''}
          </strong>{' '}
          — {entry.description}
          {reversed && (
            <span className='badge-status badge-rejeitado' style={{ marginLeft: 8 }}>
              {t('accounting.journalBook.reversed')}
            </span>
          )}
        </td>
      </tr>
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
  );
}
