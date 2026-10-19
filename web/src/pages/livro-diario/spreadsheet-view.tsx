import { Fragment } from 'react';
import { useTranslation } from 'react-i18next';
import { type JournalBook, type JournalBookLineView } from '../../api';
import { brlCents } from '../../lib';
import { fmtDate } from '../../datetime';
import { Panel, Select, cx } from '../../ui';
import { VIEW_MODEL_ORDER, type ViewModelKey } from './tabs';

const DASH = '-';

/** Spreadsheet (planilha) view of the journal book: one row per posting line (partida), grouped by
 *  entry, with Data/Descrição repeated on every line and a hyphen on the unused debit/credit side.
 *  A per-day "Total do dia" row and a final "Total geral" row act as the highlighted summary rows.
 *  The chosen display preset toggles a `.diario-planilha--<model>` modifier on the table. */
export function SpreadsheetView({
  book,
  model,
  onModelChange,
}: {
  book: JournalBook;
  model: ViewModelKey;
  onModelChange: (m: ViewModelKey) => void;
}) {
  const { t } = useTranslation();
  const col = (k: string) => t(`accounting.journalBook.spreadsheet.columns.${k}`);

  const modelPicker = (
    <label className='diario-planilha-picker'>
      <span>{t('accounting.journalBook.spreadsheet.viewModel.label')}</span>
      <Select value={model} onChange={(e) => onModelChange(e.target.value as ViewModelKey)}>
        {VIEW_MODEL_ORDER.map((m) => (
          <option key={m} value={m}>
            {t(`accounting.journalBook.spreadsheet.viewModel.${m}`)}
          </option>
        ))}
      </Select>
    </label>
  );

  return (
    <Panel title='' action={modelPicker}>
      {/* Planilha (grade numérica), não candidata a ResponsiveTable: exceção declarada — ver .table-scroll em index.css. */}
      <div className='table-scroll'>
        <table className={cx('diario-planilha', `diario-planilha--${model}`)}>
          <thead>
            <tr>
              <th>{col('date')}</th>
              <th>{col('description')}</th>
              <th>{col('debitAccount')}</th>
              <th style={{ textAlign: 'right' }}>{col('debit')}</th>
              <th>{col('creditAccount')}</th>
              <th style={{ textAlign: 'right' }}>{col('credit')}</th>
            </tr>
          </thead>
          <tbody>
            {book.days.map((day) => (
              <Fragment key={day.date}>
                {day.entries.map((entry) =>
                  entry.lines.map((line, i) => (
                    <LineRow
                      key={`${entry.id}-${i}`}
                      date={fmtDate(day.date)}
                      description={entry.description}
                      reversed={i === 0 && entry.status === 'Reversed'}
                      entryStart={i === 0}
                      line={line}
                      reversedLabel={t('accounting.journalBook.reversed')}
                    />
                  )),
                )}
                <tr className='diario-planilha-daytotal'>
                  <td colSpan={3}>
                    {t('accounting.journalBook.dayTotal')} · {fmtDate(day.date)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(day.totalDebit)}
                  </td>
                  <td className='diario-planilha-dash'>{DASH}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(day.totalCredit)}
                  </td>
                </tr>
              </Fragment>
            ))}
          </tbody>
          <tfoot>
            <tr className='diario-planilha-grandtotal'>
              <td colSpan={3}>{t('accounting.journalBook.grandTotal')}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(book.totalDebit)}
              </td>
              <td className='diario-planilha-dash'>{DASH}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(book.totalCredit)}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </Panel>
  );
}

/** One posting line (partida). A debit line fills the Conta Débito + Débito columns and dashes the
 *  credit side; a credit line does the inverse. Date + description repeat on every line so each row
 *  is self-describing; the "Estornado" badge shows only on the entry's first line. */
function LineRow({
  date,
  description,
  reversed,
  entryStart,
  line,
  reversedLabel,
}: {
  date: string;
  description: string;
  reversed: boolean;
  entryStart: boolean;
  line: JournalBookLineView;
  reversedLabel: string;
}) {
  const isDebit = line.side === 'Debit';
  const account = `${line.accountCode} · ${line.accountName}`;
  return (
    <tr className={cx(entryStart && 'diario-planilha-entrystart')}>
      <td>{date}</td>
      <td>
        {description}
        {reversed && (
          <span className='badge-status badge-rejeitado' style={{ marginLeft: 8 }}>
            {reversedLabel}
          </span>
        )}
      </td>
      <td className={cx(!isDebit && 'diario-planilha-dash')}>{isDebit ? account : DASH}</td>
      <td className={cx('amount-cell', !isDebit && 'diario-planilha-dash')} style={{ textAlign: 'right' }}>
        {isDebit ? brlCents(line.amount) : DASH}
      </td>
      <td className={cx(isDebit && 'diario-planilha-dash')}>{!isDebit ? account : DASH}</td>
      <td className={cx('amount-cell', isDebit && 'diario-planilha-dash')} style={{ textAlign: 'right' }}>
        {!isDebit ? brlCents(line.amount) : DASH}
      </td>
    </tr>
  );
}
