import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../test/renderWithProviders';
import { setViewportMatches } from '../../test/setup';
import '../../i18n';
import type { RemittanceReturnPreview, RemittanceCriticism } from '../../api';

// CnabReturnTab drives ReconciliationEntriesTable / CriticismRecordsTable off two upload mutations
// (useRemittanceReturnAutoConfirm / useRemittanceReturnCriticism) whose `onSuccess` the component
// itself wires to local state — no query returns this data directly. Mock `mutate` to call that
// captured `onSuccess` synchronously with a fixed fixture, so `fireEvent.change` on the real
// <input type=file> exercises the real onSuccess → setState → render path, no CNAB fixtures needed.
const { mockUseBankAccounts, mockAutoConfirmMutate, mockCriticismMutate } = vi.hoisted(() => ({
  mockUseBankAccounts: vi.fn(),
  mockAutoConfirmMutate: vi.fn(),
  mockCriticismMutate: vi.fn(),
}));

vi.mock('../../queries', () => ({
  useBankAccounts: mockUseBankAccounts,
  useRemittanceReturnHistory: () => ({ data: { items: [] }, isLoading: false }),
  useRemittanceReturnPreview: () => ({ mutate: vi.fn(), isPending: false }),
  useRemittanceReturnAutoConfirm: (opts?: { onSuccess?: (d: RemittanceReturnPreview) => void }) => ({
    mutate: (vars: unknown) => {
      mockAutoConfirmMutate(vars);
      opts?.onSuccess?.(PREVIEW);
    },
    isPending: false,
  }),
  useRemittanceReturnCriticism: (opts?: { onSuccess?: (d: RemittanceCriticism) => void }) => ({
    mutate: (vars: unknown) => {
      mockCriticismMutate(vars);
      opts?.onSuccess?.(CRITICISM);
    },
    isPending: false,
  }),
  useConfirmRemittanceLine: () => ({ mutate: vi.fn(), isPending: false }),
  useMarkRemittanceLineReturned: () => ({ mutate: vi.fn(), isPending: false }),
}));

// Full replace (not a partial `orig` spread) — the only two things cnab-return-tab.tsx reads from
// this module are `P` (used only as an opaque key passed to `can()`, which always returns true
// below regardless of the key) and `usePermissions` itself.
vi.mock('../../permissions', () => ({
  P: { treasuryWrite: 'treasury:write' },
  usePermissions: () => ({ can: () => true, canAny: () => true, me: null, isAdmin: true }),
}));

import { CnabReturnTab } from './cnab-return-tab';

// One entry exercising the folded field from Task 17b: reference/document/nominal value
// (relocated — was folded into `bank`'s detail cell, phone-only).
const PREVIEW: RemittanceReturnPreview = {
  trailerOk: true,
  trailerMessages: [],
  payer: null,
  counters: { total: 1, paid: 0, rejected: 0, divergent: 0, unmatched: 1, ambiguous: 0, alreadyProcessed: 0 },
  entries: [
    {
      reference: 'REF001',
      favoredName: 'Microsoft Informática Ltda.',
      favoredDocument: '60316817000103',
      method: 'Pix',
      valorNominal: 8000,
      valorEfetivado: 8000,
      effectiveDate: '2026-08-20',
      occurrenceCodes: ['00'],
      motivo: 'Liquidado',
      authCode: 'AUTH123',
      bankReference: null,
      classification: 'Unmatched',
      matchMethod: 'None',
      remittanceId: null,
      lineId: null,
      payableId: null,
      installmentId: null,
      note: null,
    },
  ],
  autoConfirmed: 0,
};

// One record exercising the folded field in CriticismRecordsTable: reference/nominal value
// (relocated — was folded into `actions`'s detail cell, phone-only).
const CRITICISM: RemittanceCriticism = {
  payer: null,
  total: 1,
  success: 0,
  rejection: 1,
  records: [
    {
      reference: 'REF002',
      favoredName: 'Microsoft Informática Ltda.',
      amount: 8000,
      occurrenceCodes: ['AG'],
      motivo: 'CPF/CNPJ inválido',
      isSuccess: false,
      bankReference: null,
      remittanceId: null,
      lineId: null,
      lineStatus: null,
    },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUseBankAccounts.mockReturnValue({
    data: [{ id: 'ba_1', alias: 'Santander - Conta Movimento' }],
    isLoading: false,
  });
});

/** Selects the file input rendered by the currently-mounted sub-tab and fires a change on it —
 *  triggers the real `runUpload` → mutation.mutate → mocked onSuccess → setState path above. */
function uploadFile() {
  const input = document.querySelector('input[type="file"]')!;
  fireEvent.change(input, { target: { files: [new File(['x'], 'retorno.ret')] } });
}

describe('CnabReturnTab desktop', () => {
  it('ReconciliationEntriesTable: mantém as 4 colunas/headers originais (Task 17b não vaza no desktop)', () => {
    renderWithProviders(
      <MemoryRouter>
        <CnabReturnTab companyId='c1' />
      </MemoryRouter>,
    );
    uploadFile();

    const headers = [...document.querySelectorAll('table thead th')].map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Título (sistema)', 'Banco (efetivado)', 'Classificação', 'Ações']);
  });

  it('CriticismRecordsTable: mantém as 3 colunas/headers originais (Task 17b não vaza no desktop)', () => {
    renderWithProviders(
      <MemoryRouter>
        <CnabReturnTab companyId='c1' />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Análise de Crítica' }));
    uploadFile();

    const headers = [...document.querySelectorAll('table thead th')].map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Lançamento', 'Parecer', 'Ações']);
  });
});

describe('CnabReturnTab no telefone', () => {
  beforeEach(() => setViewportMatches(true));

  it('ReconciliationEntriesTable: rotula referência/valor nominal em coluna própria, não sob "Banco (efetivado)" (Task 17b)', () => {
    renderWithProviders(
      <MemoryRouter>
        <CnabReturnTab companyId='c1' />
      </MemoryRouter>,
    );
    uploadFile();
    fireEvent.click(screen.getAllByRole('button', { name: /expandir/i })[0]);

    const refLabel = screen.getByText('Referência / Valor nominal', { selector: '.rt-k' });
    expect(refLabel.nextElementSibling).toHaveTextContent('REF001');
    expect(refLabel.nextElementSibling).toHaveTextContent('60316817000103');

    // O host original ("Banco (efetivado)") continua só com o valor efetivado/data/motivo/authCode —
    // não a referência do sistema dobrada dentro de si.
    const bankLabel = screen.getByText('Banco (efetivado)', { selector: '.rt-k' });
    expect(bankLabel.nextElementSibling).not.toHaveTextContent('REF001');
    expect(bankLabel.nextElementSibling).not.toHaveTextContent('60316817000103');
  });

  it('CriticismRecordsTable: rotula referência/valor em coluna própria, não sob "Ações" (Task 17b)', () => {
    renderWithProviders(
      <MemoryRouter>
        <CnabReturnTab companyId='c1' />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Análise de Crítica' }));
    uploadFile();
    fireEvent.click(screen.getAllByRole('button', { name: /expandir/i })[0]);

    const refLabel = screen.getByText('Referência / Valor', { selector: '.rt-k' });
    expect(refLabel.nextElementSibling).toHaveTextContent('REF002');

    // O host original ("Ações") não carrega mais a referência/valor dobrados dentro de si.
    const actionsLabel = screen.getByText('Ações', { selector: '.rt-k' });
    expect(actionsLabel.nextElementSibling).not.toHaveTextContent('REF002');
  });
});
