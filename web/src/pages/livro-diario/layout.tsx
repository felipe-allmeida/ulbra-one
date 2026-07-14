import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { BookOpen } from 'lucide-react';
import { useJournalBook } from '../../queries';
import { brlCents } from '../../lib';
import { EmptyState, PageHeader } from '../../ui';
import { BookFilters } from './filters';
import { DayGroup } from './day-group';

/** Livro Diário — read-only chronological book of scriptured entries (Posted/Reversed), grouped by
 *  competence day with daily and grand totals. Filters by company (or consolidated) + competence range. */
export function LivroDiarioPage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const query = useJournalBook({
    companyId: companyId || undefined,
    from: from || undefined,
    to: to || undefined,
  });
  const book = query.data;
  const hasData = (book?.days.length ?? 0) > 0;

  return (
    <>
      <PageHeader title={t('accounting.journalBook.title')} sub={t('accounting.journalBook.subtitle')}>
        <BookFilters
          companyId={companyId}
          from={from}
          to={to}
          onCompanyChange={setCompanyId}
          onFromChange={setFrom}
          onToChange={setTo}
        />
      </PageHeader>

      {query.isLoading ? (
        <div className='help'>{t('accounting.journal.loading')}</div>
      ) : !hasData || !book ? (
        <EmptyState
          icon={<BookOpen />}
          title={t('accounting.journalBook.empty.title')}
          sub={t('accounting.journalBook.empty.sub')}
        />
      ) : (
        <>
          {book.days.map((d) => (
            <DayGroup key={d.date} day={d} />
          ))}
          <div
            className='journal-grand-total'
            style={{ display: 'flex', justifyContent: 'flex-end', gap: 24, fontWeight: 700, marginTop: 16 }}
          >
            <span>{t('accounting.journalBook.grandTotal')}</span>
            <span className='amount-cell'>{brlCents(book.totalDebit)}</span>
            <span className='amount-cell'>{brlCents(book.totalCredit)}</span>
          </div>
        </>
      )}
    </>
  );
}
