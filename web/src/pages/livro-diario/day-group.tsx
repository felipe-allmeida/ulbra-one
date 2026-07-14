import { useTranslation } from 'react-i18next';
import { type JournalBookDayView } from '../../api';
import { brlCents } from '../../lib';
import { fmtDate } from '../../datetime';
import { Panel } from '../../ui';
import { EntryBlock } from './entry-block';

/** One competence day: a panel titled with the date, listing the day's entries and a totals footer. */
export function DayGroup({ day }: { day: JournalBookDayView }) {
  const { t } = useTranslation();
  return (
    <Panel title={fmtDate(day.date)}>
      {day.entries.map((e) => (
        <EntryBlock key={e.id} entry={e} />
      ))}
      <div
        className='journal-day-total'
        style={{ display: 'flex', justifyContent: 'flex-end', gap: 24, fontWeight: 600, paddingTop: 8 }}
      >
        <span>{t('accounting.journalBook.dayTotal')}</span>
        <span className='amount-cell'>{brlCents(day.totalDebit)}</span>
        <span className='amount-cell'>{brlCents(day.totalCredit)}</span>
      </div>
    </Panel>
  );
}
