import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const navigate = vi.fn();
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigate };
});

const { autoConfirm, confirmLine } = vi.hoisted(() => ({
  autoConfirm: { onSuccess: null as ((d: unknown) => void) | null },
  confirmLine: vi.fn(),
}));

vi.mock('../../queries', () => ({
  useBankAccounts: () => ({ data: [{ id: 'ba_1', alias: 'Banrisul c/c 41.221' }] }),
  useRemittanceReturnHistory: () => ({ data: [], isLoading: false }),
  useRemittanceReturnCriticism: () => ({ mutate: vi.fn(), isPending: false }),
  useRemittanceReturnPreview: () => ({ mutate: vi.fn(), isPending: false }),
  useRemittanceReturnAutoConfirm: (opts: { onSuccess: (d: unknown) => void }) => {
    autoConfirm.onSuccess = opts.onSuccess;
    return { mutate: () => autoConfirm.onSuccess?.(preview), isPending: false };
  },
  useConfirmRemittanceLine: () => ({ mutate: confirmLine, isPending: false }),
  useMarkRemittanceLineReturned: () => ({ mutate: vi.fn(), isPending: false }),
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
}));
vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof import('../../permissions')>()),
  usePermissions: () => ({ me: { isAdmin: true }, isAdmin: true, can: () => true, canAny: () => true }),
}));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

import { CnabReturnTab } from './cnab-return-tab';

const entry = (over: Record<string, unknown>) => ({
  reference: 'PAG-0004821',
  favoredName: 'Papelaria Central LTDA',
  favoredDocument: '12345678000195',
  method: 'Ted',
  valorNominal: 4280,
  valorEfetivado: 4280,
  effectiveDate: '2026-08-26',
  occurrenceCodes: ['00'],
  motivo: null,
  authCode: null,
  bankReference: null,
  classification: 'Paid',
  matchMethod: 'Reference',
  remittanceId: 'rem_1',
  lineId: 'line_1',
  payableId: 'payable_495ba26af7804d4b9dc9ff2cc06ed411',
  installmentId: 'inst_1',
  note: null,
  ...over,
});

const preview = {
  trailerOk: true,
  trailerMessages: [],
  payer: null,
  autoConfirmed: 0,
  counters: { total: 2, paid: 1, rejected: 0, divergent: 0, ambiguous: 0, unmatched: 1 },
  entries: [
    entry({}),
    entry({
      reference: 'PAG-0004822',
      favoredName: 'Tecnoserv Manutenção',
      classification: 'Unmatched',
      matchMethod: 'None',
      valorNominal: null,
      valorEfetivado: 890,
      remittanceId: null,
      lineId: null,
      payableId: null,
      installmentId: null,
    }),
  ],
};

/** Sobe o retorno: escolhe a conta pagadora e dispara o upload (o mock resolve com `preview`). */
function renderWithPreview() {
  const view = render(
    <MemoryRouter initialEntries={['/app/treasury?tab=cnabReturn']}>
      <CnabReturnTab companyId='c1' />
    </MemoryRouter>,
  );
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'ba_1' } });
  const file = new File(['x'], 'retorno.RET', { type: 'text/plain' });
  const input = document.querySelector('input[type="file"]')!;
  fireEvent.change(input, { target: { files: [file] } });
  return view;
}

describe('CnabReturnTab — linha de retorno', () => {
  beforeEach(() => {
    navigate.mockClear();
    confirmLine.mockClear();
  });

  it('mostra as duas linhas do retorno', () => {
    renderWithPreview();

    expect(screen.getByText('Papelaria Central LTDA')).toBeInTheDocument();
    expect(screen.getByText('Tecnoserv Manutenção')).toBeInTheDocument();
  });

  /**
   * A linha tem a mesma anatomia das outras tabelas do app (favorecido, valor, referência), mas não
   * tinha handler nenhum — o operador clicava esperando abrir o título e não acontecia nada.
   */
  it('abre o título quando a linha casou com um payable', () => {
    renderWithPreview();

    fireEvent.click(screen.getByText('Papelaria Central LTDA'));

    expect(navigate).toHaveBeenCalledWith('/app/payables/payable_495ba26af7804d4b9dc9ff2cc06ed411', {
      state: { from: '/app/treasury?tab=cnabReturn' },
      replace: undefined,
    });
  });

  it('não navega quando a linha não casou com nenhum título', () => {
    renderWithPreview();

    fireEvent.click(screen.getByText('Tecnoserv Manutenção'));

    expect(navigate).not.toHaveBeenCalled();
  });

  it('não abre o título quando o clique cai no botão de ação', () => {
    renderWithPreview();

    fireEvent.click(screen.getByRole('button', { name: /Confirmar/i }));

    expect(confirmLine).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalled();
  });

  /** A célula de ações ficava vazia — clicar ali morria sem dizer por quê. */
  it('explica a ausência de ação quando nada casou, em vez de deixar a célula vazia', () => {
    renderWithPreview();

    expect(screen.getByText(/nenhum título/i)).toBeInTheDocument();
  });
});

describe('CnabReturnTab — classificação', () => {
  it('usa os badges do design system em vez de cor inline', () => {
    renderWithPreview();

    expect(screen.getByText('Pago')).toHaveClass('badge-status', 'badge-pago');
    expect(screen.getByText('Não localizado')).toHaveClass('badge-status', 'badge-neutral');
  });
});
