import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '../../i18n';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../test/renderWithProviders';
import type * as Permissions from '../../permissions';

const { mockMatch, mockConfirm } = vi.hoisted(() => ({ mockMatch: vi.fn(), mockConfirm: vi.fn() }));

vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof Permissions>()),
  usePermissions: () => ({ can: () => true }),
}));
vi.mock('../../queries', () => ({
  useConfirmMatch: () => ({ mutate: mockConfirm, isPending: false }),
  useMatchTransaction: () => ({ mutate: mockMatch, isPending: false }),
  useIgnoreTransaction: () => ({ mutate: vi.fn(), isPending: false }),
  useUnmatchTransaction: () => ({ mutate: vi.fn(), isPending: false }),
}));

import { TransactionsTable } from './transactions-table';
import { type BankTransaction } from '../../api';

const base = {
  bankAccountId: 'bank_account_1',
  date: '2026-08-25',
  direction: 'Debit' as const,
  matchedPaymentId: null,
  paymentPreexisting: false,
  createdAt: '2026-08-26T10:00:00Z',
  suggestedInstallmentId: null,
  candidates: [],
};

const candidate = {
  installmentId: 'installment_1',
  payableId: 'payable_1',
  counterpartyName: 'CONSTRUTORA HORIZONTE LTDA',
  documentNumber: 'NF 12345',
  sequence: 1,
  installmentCount: 3,
  remaining: 442.36,
  dueDate: '2026-08-25',
  amountDelta: 10.85,
  dayDelta: 0,
};

const fee: BankTransaction = {
  ...base,
  id: 'bank_txn_fee',
  amount: -9.66,
  description: 'TAR REG TIT COB RAPIDA SIMP-ELETR',
  matchStatus: 'Unmatched',
  mismatch: 'NoCandidate',
};

const divergent: BankTransaction = {
  ...base,
  id: 'bank_txn_div',
  amount: -431.51,
  description: 'PAGFOR PIX OUTRA INST- DIFEREN TIT 3578.4906393627',
  matchStatus: 'Unmatched',
  mismatch: 'AmountDiffers',
  candidates: [candidate],
};

const render = (items: BankTransaction[], stage: 'analysis' | 'resolved' = 'analysis') =>
  // A reconciled row links to its título, so the tree needs a Router (repo pattern: wrap locally).
  renderWithProviders(
    <MemoryRouter>
      <TransactionsTable items={items} page={1} totalPages={1} stage={stage} onPage={vi.fn()} onManual={vi.fn()} />
    </MemoryRouter>,
  );

beforeEach(() => {
  mockMatch.mockReset();
  mockConfirm.mockReset();
});

describe('TransactionsTable', () => {
  it('shows the mismatch reason instead of repeating "Não conciliado" on every analysis row', () => {
    render([fee, divergent]);
    expect(screen.getByText('Motivo')).toBeInTheDocument();
    expect(screen.getByText('Sem título')).toBeInTheDocument();
    expect(screen.getByText('Valor difere')).toBeInTheDocument();
    expect(screen.queryByText('Não conciliado')).not.toBeInTheDocument();
  });

  it('keeps the plain status column on the resolved tab, where the reason no longer applies', () => {
    const done: BankTransaction = {
      ...base,
      id: 'bank_txn_done',
      amount: -100,
      description: 'PAGAMENTO DE TITULO',
      matchStatus: 'Reconciled',
      mismatch: null,
    };
    render([done], 'resolved');
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText('Conciliado')).toBeInTheDocument();
  });

  it('does not offer candidates on a line that is already reconciled', async () => {
    // The panel used to test only for 'Suggested', so a Reconciled line fell into the "still looking" branch
    // and was shown "Nenhum título candidato no período" plus a button to link another parcela — on a line
    // that is already linked to a payment.
    const linked: BankTransaction = {
      ...base,
      id: 'bank_txn_linked',
      amount: -150,
      description: 'PAGAMENTO DE TITULO 3578.4906393627',
      matchStatus: 'Reconciled',
      paymentPreexisting: true,
      mismatch: null,
      matchedTitle: {
        payableId: 'payable_d8a21e84',
        counterpartyName: 'AR SMARTVIX SERVICOS DE CERTIFICACAO DIGITAL LTDA',
        documentNumber: null,
        sequence: 1,
        installmentCount: 1,
        dueDate: '2026-08-30',
        amount: 150,
        paymentDate: '2026-08-25',
        notes: 'CNAB retorno',
      },
    };
    const user = userEvent.setup();
    render([linked], 'resolved');

    await user.click(screen.getByText(/PAGAMENTO DE TITULO/));

    expect(screen.queryByText('Títulos candidatos')).not.toBeInTheDocument();
    expect(screen.queryByText('Nenhum título candidato no período.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Buscar outra parcela/ })).not.toBeInTheDocument();
    // The pairing itself: conta a pagar × extrato, not merely "was reconciled".
    expect(screen.getByText('AR SMARTVIX SERVICOS DE CERTIFICACAO DIGITAL LTDA')).toBeInTheDocument();
    expect(screen.getByText(/1\/1/)).toBeInTheDocument();
    expect(screen.getByText(/Vinculado a um pagamento já registrado/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Abrir o título' })).toBeInTheDocument();
  });

  it('says an ignored line was ignored instead of hunting for títulos', async () => {
    const ignored: BankTransaction = {
      ...base,
      id: 'bank_txn_ign',
      amount: -9.66,
      description: 'TAR REG TIT COB RAPIDA',
      matchStatus: 'Ignored',
      mismatch: null,
    };
    const user = userEvent.setup();
    render([ignored], 'resolved');

    await user.click(screen.getByText(/TAR REG TIT/));

    expect(screen.queryByText('Títulos candidatos')).not.toBeInTheDocument();
    expect(screen.getByText(/marcado como sem título correspondente/)).toBeInTheDocument();
  });

  it('gives every icon action an accessible name, since the visible labels are gone', () => {
    render([divergent]);
    expect(screen.getByRole('button', { name: 'Conciliar manual' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ignorar' })).toBeInTheDocument();
  });

  it('reveals the candidate and its deltas when the row is expanded', async () => {
    const user = userEvent.setup();
    render([divergent]);
    expect(screen.queryByText('CONSTRUTORA HORIZONTE LTDA')).not.toBeInTheDocument();

    await user.click(screen.getByText(/PAGFOR PIX/));

    expect(screen.getByText('CONSTRUTORA HORIZONTE LTDA')).toBeInTheDocument();
    expect(screen.getByText(/Parcela 1\/3/)).toBeInTheDocument();
    expect(screen.getByText(/de diferença/)).toBeInTheDocument();
    expect(screen.getByText('Mesmo dia')).toBeInTheDocument();
  });

  it('explains a line that has no candidate at all, rather than showing an empty list', async () => {
    const user = userEvent.setup();
    render([fee]);
    await user.click(screen.getByText(/TAR REG TIT/));
    expect(screen.getByText(/tarifa, IOF ou imposto/)).toBeInTheDocument();
    expect(screen.getByText('Nenhum título candidato no período.')).toBeInTheDocument();
  });

  it('reconciles straight from a candidate, without going through the manual drawer', async () => {
    const user = userEvent.setup();
    render([divergent]);
    await user.click(screen.getByText(/PAGFOR PIX/));
    await user.click(screen.getByRole('button', { name: 'Conciliar com este' }));
    expect(mockMatch).toHaveBeenCalledWith({ id: 'bank_txn_div', installmentId: 'installment_1' });
  });

  it('confirms the suggestion rather than re-matching it when the matcher already picked one', async () => {
    const user = userEvent.setup();
    const suggested: BankTransaction = {
      ...base,
      id: 'bank_txn_sug',
      amount: -442.36,
      description: 'PAGAMENTO DE TITULO 3578.4906393627',
      matchStatus: 'Suggested',
      suggestedInstallmentId: 'installment_1',
      mismatch: null,
      candidates: [{ ...candidate, amountDelta: 0 }],
    };
    const { container } = render([suggested]);
    await user.click(screen.getByText(/PAGAMENTO DE TITULO/));
    // "Confirmar" is offered twice on a suggested row — the row icon and the detail button — so scope to the body.
    const detail = within(container.querySelector('.cc-detail-row')!);
    await user.click(detail.getByRole('button', { name: 'Confirmar' }));
    expect(mockConfirm).toHaveBeenCalledWith({ id: 'bank_txn_sug' });
    expect(mockMatch).not.toHaveBeenCalled();
  });

  it('does not expand the row when a row action is clicked', async () => {
    const user = userEvent.setup();
    render([divergent]);
    await user.click(screen.getByRole('button', { name: 'Ignorar' }));
    expect(screen.queryByText('CONSTRUTORA HORIZONTE LTDA')).not.toBeInTheDocument();
  });

  /** O clique tem que morrer no botão, não na célula: o espaço em volta dele era zona morta. */
  it('expands the row when the click lands in the actions column but misses a button', async () => {
    const user = userEvent.setup();
    render([divergent]);
    const cells = screen
      .getByText(/PAGFOR PIX/)
      .closest('tr')!
      .querySelectorAll('td');
    await user.click(cells[cells.length - 1]);
    expect(screen.getByText('CONSTRUTORA HORIZONTE LTDA')).toBeInTheDocument();
  });

  it('opens only one row at a time, so the list stays scannable', async () => {
    const user = userEvent.setup();
    render([fee, divergent]);
    await user.click(screen.getByText(/PAGFOR PIX/));
    expect(screen.getByText('CONSTRUTORA HORIZONTE LTDA')).toBeInTheDocument();

    await user.click(screen.getByText(/TAR REG TIT/));
    expect(screen.queryByText('CONSTRUTORA HORIZONTE LTDA')).not.toBeInTheDocument();
  });

  it('expands from the keyboard, because the row is the primary way in', async () => {
    const user = userEvent.setup();
    render([divergent]);
    const row = screen.getByText(/PAGFOR PIX/).closest('tr')!;
    row.focus();
    await user.keyboard('{Enter}');
    expect(within(row.parentElement!).getByText('CONSTRUTORA HORIZONTE LTDA')).toBeInTheDocument();
  });
});
