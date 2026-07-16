import { describe, it, expect, vi } from 'vitest';
import { renderWithProviders } from '../../test/renderWithProviders';
import { type JournalBook } from '../../api';
import { SpreadsheetView } from './spreadsheet-view';

const noop = vi.fn();

const book: JournalBook = {
  companyId: 'company_1',
  from: '2026-07-01',
  to: '2026-07-31',
  totalDebit: 300,
  totalCredit: 300,
  days: [
    {
      date: '2026-07-01',
      totalDebit: 300,
      totalCredit: 300,
      entries: [
        {
          id: 'journal_1',
          number: 1,
          competenceDate: '2026-07-01',
          description: 'NF 235 fornecedor',
          status: 'Posted',
          totalDebit: 300,
          totalCredit: 300,
          lines: [
            { ledgerAccountId: 'ledgacct_1', accountCode: '1.1.3', accountName: 'Estoque', side: 'Debit', amount: 300, costCenterId: null },
            { ledgerAccountId: 'ledgacct_2', accountCode: '2.1.1', accountName: 'Fornecedores', side: 'Credit', amount: 291, costCenterId: null },
            { ledgerAccountId: 'ledgacct_3', accountCode: '2.1.2', accountName: 'ISS Retido', side: 'Credit', amount: 9, costCenterId: null },
          ],
        },
      ],
    },
  ],
};

describe('SpreadsheetView', () => {
  it('renders one row per posting line plus a day-total and grand-total row', () => {
    const { container } = renderWithProviders(
      <SpreadsheetView book={book} model='modelo1' onModelChange={noop} />,
    );
    // 3 partida rows + 1 day-total row live in the body; grand-total is in the footer.
    expect(container.querySelectorAll('tbody tr')).toHaveLength(4);
    expect(container.querySelectorAll('.diario-planilha-entrystart')).toHaveLength(1);
    expect(container.querySelectorAll('.diario-planilha-daytotal')).toHaveLength(1);
    expect(container.querySelectorAll('.diario-planilha-grandtotal')).toHaveLength(1);
  });

  it('puts the account on its side and dashes the unused side', () => {
    const { container } = renderWithProviders(
      <SpreadsheetView book={book} model='modelo1' onModelChange={noop} />,
    );
    const rows = container.querySelectorAll('tbody tr');
    const debit = rows[0].querySelectorAll('td');
    expect(debit[2].textContent).toContain('Estoque');
    expect(debit[4].textContent).toBe('-');
    expect(debit[5].textContent).toBe('-');
    const credit = rows[1].querySelectorAll('td');
    expect(credit[2].textContent).toBe('-');
    expect(credit[3].textContent).toBe('-');
    expect(credit[4].textContent).toContain('Fornecedores');
  });

  it('repeats date and description on every line of the entry', () => {
    const { container } = renderWithProviders(
      <SpreadsheetView book={book} model='modelo1' onModelChange={noop} />,
    );
    const rows = container.querySelectorAll('tbody tr');
    for (const i of [0, 1, 2]) {
      const cells = rows[i].querySelectorAll('td');
      expect(cells[0].textContent).not.toBe('');
      expect(cells[1].textContent).toContain('NF 235 fornecedor');
    }
  });

  it('shows the reversed badge only on the first line of a reversed entry', () => {
    const reversed: JournalBook = {
      ...book,
      days: [{ ...book.days[0], entries: [{ ...book.days[0].entries[0], status: 'Reversed' }] }],
    };
    const { container } = renderWithProviders(
      <SpreadsheetView book={reversed} model='modelo1' onModelChange={noop} />,
    );
    expect(container.querySelectorAll('.badge-rejeitado')).toHaveLength(1);
  });

  it('applies the chosen display model class to the table', () => {
    const { container } = renderWithProviders(
      <SpreadsheetView book={book} model='modelo2' onModelChange={noop} />,
    );
    const table = container.querySelector('table.diario-planilha');
    expect(table?.classList.contains('diario-planilha--modelo2')).toBe(true);
  });
});
