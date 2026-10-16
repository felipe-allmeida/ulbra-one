import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';
import { type Requisition } from '../../api';

const { mockAdd, mockTerminate } = vi.hoisted(() => ({ mockAdd: vi.fn(), mockTerminate: vi.fn() }));
vi.mock('../../queries', () => ({
  useAddRecurrenceCycle: () => ({ mutate: mockAdd, isPending: false }),
  useTerminateRecurrence: () => ({ mutate: mockTerminate, isPending: false }),
  // The launch form embeds the boleto reader — stub its hooks (no file is selected in these tests).
  useRequestBoletoUploadUrl: () => ({ mutateAsync: vi.fn(), reset: vi.fn(), isPending: false }),
  useParseBoleto: () => ({ mutateAsync: vi.fn(), reset: vi.fn(), isPending: false }),
  uploadToPresignedUrl: vi.fn(),
  useVendor: () => ({ data: undefined }),
}));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

import { RecurrenceCyclesPanel } from './recurrence-cycles';

const baseReq = {
  id: 'req_1',
  status: 'InPayment',
  type: 'FixedExpense',
  startCompetency: '2026-07',
  estimateValue: 1000,
  firstDueDate: '2026-07-10',
  basePayableId: 'payable-base',
  baseRealizedAmount: 1000, // base totalmente pago → "Pago"
  cycles: [
    {
      sequence: 1,
      competency: '2026-08',
      amount: 900,
      realizedAmount: 400, // parcial
      dueDate: '2026-08-10',
      paymentMethod: 'Boleto',
      boletoDigitableLine: '846700000017599200241114202410000000000000000000', // convênio (começa com 8)
      payableId: 'payable-aug',
      createdAt: '2026-08-01T00:00:00Z',
    },
  ],
} as unknown as Requisition;

const renderPanel = (canLaunch = true) =>
  render(
    <MemoryRouter>
      <RecurrenceCyclesPanel req={baseReq} canLaunch={canLaunch} />
    </MemoryRouter>,
  );

describe('RecurrenceCyclesPanel', () => {
  beforeEach(() => {
    mockAdd.mockReset();
    mockTerminate.mockReset();
  });

  it('mostra o status pago/parcial por ciclo (realizado × devido)', () => {
    renderPanel();
    expect(screen.getByText('Pago')).toBeInTheDocument(); // base 1000/1000
    expect(screen.getByText('Parcial')).toBeInTheDocument(); // ciclo 400/900
  });

  it('lista o ciclo-base e o ciclo lançado, sinalizando o convênio e os títulos', () => {
    renderPanel();
    expect(screen.getByText('07/2026')).toBeInTheDocument(); // base
    expect(screen.getByText('08/2026')).toBeInTheDocument(); // ciclo de agosto
    expect(screen.getByText('Convênio')).toBeInTheDocument(); // boleto 8… do ciclo
    // base + ciclo têm payableId → dois links "Ver título".
    expect(screen.getAllByText('Ver título')).toHaveLength(2);
  });

  it('lança a fatura do mês com a competência sugerida (última + 1) e o valor', () => {
    renderPanel();
    fireEvent.change(screen.getByLabelText('Vencimento'), { target: { value: '2026-09-10' } });
    fireEvent.change(screen.getByLabelText('Valor'), { target: { value: '850' } });
    // A fatura do mês é obrigatória (AEL-959) — anexar antes de lançar.
    const fileInput = document.querySelector('input[type="file"]')!;
    fireEvent.change(fileInput, { target: { files: [new File(['x'], 'fatura.pdf', { type: 'application/pdf' })] } });
    fireEvent.click(screen.getByRole('button', { name: 'Lançar fatura' }));

    expect(mockAdd).toHaveBeenCalledTimes(1);
    const [vars] = mockAdd.mock.calls[0];
    expect(vars.id).toBe('req_1');
    expect(vars.body).toMatchObject({ competency: '2026-09', amount: 850, paymentMethod: 'Pix' });
    expect(vars.file).toBeInstanceOf(File); // a fatura do mês viaja no lançamento
  });

  it('exige a fatura do mês antes de lançar (Conta Fixa)', () => {
    renderPanel();
    fireEvent.change(screen.getByLabelText('Vencimento'), { target: { value: '2026-09-10' } });
    fireEvent.change(screen.getByLabelText('Valor'), { target: { value: '850' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lançar fatura' })); // sem anexar a fatura

    expect(mockAdd).not.toHaveBeenCalled();
    expect(screen.getByText('Anexe a fatura do mês.')).toBeInTheDocument();
  });

  it('esconde o formulário de lançamento para quem não pode lançar', () => {
    renderPanel(false);
    expect(screen.queryByText('Lançar fatura do mês')).not.toBeInTheDocument();
    // Mas a lista continua visível (somente leitura).
    expect(screen.getByText('08/2026')).toBeInTheDocument();
  });
});
