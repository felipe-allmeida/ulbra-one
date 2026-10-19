import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { BookOpen } from 'lucide-react';
import { useApp } from '../../store';
import { useJournalBook } from '../../queries';
import { startOfCurrentMonth, endOfCurrentMonth } from '../../datetime';
import { brlCents } from '../../lib';
import { EmptyState, Panel, LoadingState } from '../../ui';
import { StageTabBar, type StageTab } from '../../components/workflow';
import { ReportHead } from '../contabil-shared';
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
 *  over the same data: "Planilha" (a single columnar spreadsheet with selectable display models,
 *  the default view) and "Cronológico" (per-day panels). Active tab (`?tab=`) and spreadsheet
 *  model (`?view=`) live in the URL. The empresa/consolidado scope comes from the topbar selector
 *  (`useApp`); the page filters only by competence range + unidade. */
export function LivroDiarioPage() {
  const { t } = useTranslation();
  const { scopeCompanyId, scopeReady } = useApp();
  const [from, setFrom] = useState(startOfCurrentMonth);
  const [to, setTo] = useState(endOfCurrentMonth);
  const [unitId, setUnitId] = useState('');
  const [params, setParams] = useSearchParams();

  const tabParam = params.get('tab');
  const tab: JournalTabKey = isTabKey(tabParam) ? tabParam : DEFAULT_TAB;
  const modelParam = params.get('view');
  const model: ViewModelKey = isViewModelKey(modelParam) ? modelParam : DEFAULT_VIEW_MODEL;

  const setTab = (key: string) =>
    setParams((prev) => {
      prev.set('tab', key);
      return prev;
    });
  const setModel = (m: ViewModelKey) =>
    setParams((prev) => {
      prev.set('view', m);
      return prev;
    });

  const query = useJournalBook(
    {
      companyId: scopeCompanyId,
      from: from || undefined,
      to: to || undefined,
      unitId: unitId || undefined,
    },
    { enabled: scopeReady },
  );
  const book = query.data;
  const hasData = (book?.days.length ?? 0) > 0;

  const tabs: StageTab[] = [
    { key: 'spreadsheet', label: t('accounting.journalBook.tabs.spreadsheet') },
    { key: 'chronological', label: t('accounting.journalBook.tabs.chronological') },
  ];

  return (
    <>
      <ReportHead title={t('accounting.journalBook.title')} sub={t('accounting.journalBook.subtitle')}>
        <BookFilters
          from={from}
          to={to}
          unitId={unitId}
          onFromChange={setFrom}
          onToChange={setTo}
          onUnitChange={setUnitId}
        />
      </ReportHead>

      <StageTabBar tabs={tabs} active={tab} onChange={setTab} />

      {!scopeReady || query.isLoading ? (
        <LoadingState />
      ) : !hasData || !book ? (
        <EmptyState
          icon={<BookOpen />}
          title={t('accounting.journalBook.empty.title')}
          sub={t('accounting.journalBook.empty.sub')}
        />
      ) : tab === 'spreadsheet' ? (
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
