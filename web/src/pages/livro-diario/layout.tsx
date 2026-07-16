import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { BookOpen } from 'lucide-react';
import { useJournalBook } from '../../queries';
import { brlCents } from '../../lib';
import { EmptyState, PageHeader, Panel } from '../../ui';
import { StageTabBar, type StageTab } from '../../components/workflow';
import { BookFilters } from './filters';
import { DayGroup } from './day-group';
import { SpreadsheetView } from './spreadsheet-view';
import {
  DEFAULT_TAB,
  DEFAULT_VIEW_MODEL,
  isTabKey,
  isViewModelKey,
  type JournalTabKey,
  type ViewModelKey,
} from './tabs';

/** Livro Diário — read-only chronological book of scriptured entries (Posted/Reversed). Two tabs
 *  over the same data: "Livro" (per-day panels) and "Planilha" (a single columnar spreadsheet with
 *  selectable display models). Active tab (`?tab=`) and spreadsheet model (`?modelo=`) live in the URL.
 *  Filters by company (or consolidated) + competence range. */
export function LivroDiarioPage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [params, setParams] = useSearchParams();

  const tabParam = params.get('tab');
  const tab: JournalTabKey = isTabKey(tabParam) ? tabParam : DEFAULT_TAB;
  const modelParam = params.get('modelo');
  const model: ViewModelKey = isViewModelKey(modelParam) ? modelParam : DEFAULT_VIEW_MODEL;

  const setTab = (key: string) =>
    setParams((prev) => {
      prev.set('tab', key);
      return prev;
    });
  const setModel = (m: ViewModelKey) =>
    setParams((prev) => {
      prev.set('modelo', m);
      return prev;
    });

  const query = useJournalBook({
    companyId: companyId || undefined,
    from: from || undefined,
    to: to || undefined,
  });
  const book = query.data;
  const hasData = (book?.days.length ?? 0) > 0;

  const tabs: StageTab[] = [
    { key: 'planilha', label: t('accounting.journalBook.tabs.spreadsheet') },
    { key: 'cronologico', label: t('accounting.journalBook.tabs.chronological') },
  ];

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

      <StageTabBar tabs={tabs} active={tab} onChange={setTab} />

      {query.isLoading ? (
        <div className='help'>{t('accounting.journal.loading')}</div>
      ) : !hasData || !book ? (
        <EmptyState
          icon={<BookOpen />}
          title={t('accounting.journalBook.empty.title')}
          sub={t('accounting.journalBook.empty.sub')}
        />
      ) : tab === 'planilha' ? (
        <SpreadsheetView book={book} model={model} onModelChange={setModel} />
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {book.days.map((d) => (
            <DayGroup key={d.date} day={d} />
          ))}
          <Panel title={t('accounting.journalBook.grandTotal')}>
            <div
              style={{
                display: 'flex',
                justifyContent: 'flex-end',
                gap: 24,
                fontWeight: 700,
                padding: '11px 16px',
              }}
            >
              <span className='amount-cell'>{brlCents(book.totalDebit)}</span>
              <span className='amount-cell'>{brlCents(book.totalCredit)}</span>
            </div>
          </Panel>
        </div>
      )}
    </>
  );
}
