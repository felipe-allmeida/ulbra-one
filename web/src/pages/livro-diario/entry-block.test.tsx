import { describe, it, expect } from 'vitest';
import { renderWithProviders } from '../../test/renderWithProviders';
import { type JournalBookEntryView } from '../../api';
import { EntryBlock } from './entry-block';

const entry: JournalBookEntryView = {
  id: 'journal_1',
  number: 7,
  competenceDate: '2026-07-01',
  description: 'Provisão de despesa',
  status: 'Posted',
  totalDebit: 100,
  totalCredit: 100,
  lines: [
    { ledgerAccountId: 'ledgacct_1', accountCode: '4.1', accountName: 'Despesa', side: 'Debit', amount: 100 },
    { ledgerAccountId: 'ledgacct_2', accountCode: '2.1', accountName: 'Fornecedores', side: 'Credit', amount: 100 },
  ],
};

describe('EntryBlock', () => {
  it('renders the entry number, description and both D/C lines', () => {
    const { container } = renderWithProviders(<EntryBlock entry={entry} />);
    expect(container.textContent).toContain('#7');
    expect(container.textContent).toContain('Provisão de despesa');
    expect(container.textContent).toContain('4.1');
    expect(container.textContent).toContain('Fornecedores');
  });

  it('flags a reversed entry with the rejected badge', () => {
    const { container } = renderWithProviders(<EntryBlock entry={{ ...entry, status: 'Reversed' }} />);
    expect(container.querySelector('.badge-rejeitado')).toBeInTheDocument();
  });

  it('does not flag a posted entry', () => {
    const { container } = renderWithProviders(<EntryBlock entry={entry} />);
    expect(container.querySelector('.badge-rejeitado')).toBeNull();
  });
});
