import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // pt-BR labels.

const { mutate } = vi.hoisted(() => ({ mutate: vi.fn() }));

vi.mock('../../queries', () => ({
  useBankAccounts: () => ({
    data: [{ id: 'bank_1', alias: 'Conta Remessa', bankCode: '033', cnabAgreementCode: '123456789012', active: true }],
    isLoading: false,
  }),
  useCreateRemittance: () => ({ mutate, isPending: false }),
}));

import { GenerateDialog } from './generate-dialog';

/** The Field label is not tied to its input by htmlFor — reach the input through the field wrapper. */
const inputFor = (label: string) => screen.getByText(label).closest('.form-field')!.querySelector('input')!;

const selectAccount = () => {
  const select = screen.getByText('Conta pagadora').closest('.form-field')!.querySelector('select')!;
  fireEvent.change(select, { target: { value: 'bank_1' } });
};

const submit = () => fireEvent.submit(document.getElementById('generate-remittance-form')!);

beforeEach(() => {
  mutate.mockClear();
});

describe('GenerateDialog — data de pagamento', () => {
  it('envia a data de pagamento escolhida no modo corte', () => {
    renderWithProviders(<GenerateDialog companyId='c1' onClose={vi.fn()} />);
    selectAccount();
    fireEvent.change(inputFor('Data de pagamento (opcional)'), { target: { value: '2026-09-15' } });
    submit();

    expect(mutate).toHaveBeenCalledWith(expect.objectContaining({ paymentDate: '2026-09-15' }));
  });

  it('envia a data de pagamento também no modo seleção, onde o corte não aparece', () => {
    renderWithProviders(<GenerateDialog companyId='c1' installmentIds={['ins_1', 'ins_2']} onClose={vi.fn()} />);
    selectAccount();
    // Selection mode hides the cutoff — the payment date is not tied to it and must stay.
    expect(screen.queryByText('Vencimento até (opcional)')).not.toBeInTheDocument();
    fireEvent.change(inputFor('Data de pagamento (opcional)'), { target: { value: '2026-09-15' } });
    submit();

    expect(mutate).toHaveBeenCalledWith(
      expect.objectContaining({ paymentDate: '2026-09-15', installmentIds: ['ins_1', 'ins_2'] }),
    );
  });

  it('manda null quando a data fica em branco — o servidor usa hoje', () => {
    renderWithProviders(<GenerateDialog companyId='c1' onClose={vi.fn()} />);
    selectAccount();
    submit();

    expect(mutate).toHaveBeenCalledWith(expect.objectContaining({ paymentDate: null }));
  });

  it('não deixa escolher uma data no passado', () => {
    renderWithProviders(<GenerateDialog companyId='c1' onClose={vi.fn()} />);
    const today = new Date().toISOString().slice(0, 10);
    expect(inputFor('Data de pagamento (opcional)')).toHaveAttribute('min', today);
  });
});
