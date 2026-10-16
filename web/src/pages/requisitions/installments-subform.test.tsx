import { describe, it, expect, vi } from 'vitest';
import { type ReactElement } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import '../../i18n';
import { type Row, InstallmentsSubform, buildInstallmentsPayload, validateInstallments } from './installments-subform';

const row = (over: Partial<Row> = {}): Row => ({
  dueDate: '2026-07-10',
  amount: '100',
  paymentMethod: 'Pix',
  boletoDigitableLine: '',
  ...over,
});

// The subform embeds the boleto reader (React Query hooks), so renders need a QueryClientProvider.
const renderWithClient = (ui: ReactElement) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
      {ui}
    </QueryClientProvider>,
  );

describe('validateInstallments', () => {
  it('valida um plano correto', () => {
    expect(validateInstallments([row()], '2026-07-05').valid).toBe(true);
  });

  it('exige data de emissão', () => {
    const v = validateInstallments([row()], '');
    expect(v.issueMissing).toBe(true);
    expect(v.valid).toBe(false);
  });

  it('emissão não pode ser após o 1º vencimento', () => {
    const v = validateInstallments([row({ dueDate: '2026-07-10' })], '2026-08-01');
    expect(v.issueAfterDue).toBe(true);
    expect(v.valid).toBe(false);
  });

  it('boleto preenchido precisa ter 47/48 dígitos', () => {
    expect(
      validateInstallments([row({ paymentMethod: 'Boleto', boletoDigitableLine: '123' })], '2026-07-05').boletoValid,
    ).toBe(false);
  });

  it('linhas sem valor ou lista vazia são inválidas', () => {
    expect(validateInstallments([row({ amount: '0' })], '2026-07-05').rowsValid).toBe(false);
    expect(validateInstallments([], '2026-07-05').rowsValid).toBe(false);
  });
});

describe('buildInstallmentsPayload', () => {
  it('omite a linha digitável em métodos não-boleto', () => {
    const [p] = buildInstallmentsPayload([row({ paymentMethod: 'Pix', boletoDigitableLine: '999' })]);
    expect(p).toEqual({ dueDate: '2026-07-10', amount: 100, paymentMethod: 'Pix' });
  });

  it('inclui só os dígitos da linha digitável no boleto', () => {
    const line = '34191.79001 01043.510047 91020.150008 2 91070026000';
    const [p] = buildInstallmentsPayload([row({ paymentMethod: 'Boleto', boletoDigitableLine: line })]);
    expect(p.boletoDigitableLine).toBe('34191790010104351004791020150008291070026000');
  });
});

describe('InstallmentsSubform', () => {
  const base = {
    companyId: 'company_1',
    issueDate: '2026-07-05',
    onIssueDateChange: vi.fn(),
    recurring: false,
    firstDueDate: '2026-07-10',
    fallbackTotal: 100,
    showErrors: false,
  };

  it('adiciona uma parcela', () => {
    const onRowsChange = vi.fn();
    renderWithClient(<InstallmentsSubform {...base} rows={[row()]} onRowsChange={onRowsChange} />);
    fireEvent.click(screen.getByRole('button', { name: /adicionar parcela/i }));
    expect(onRowsChange).toHaveBeenCalledWith([row(), expect.objectContaining({ dueDate: '2026-07-10', amount: '' })]);
  });

  it('trocar o método para não-boleto zera a linha digitável', () => {
    const onRowsChange = vi.fn();
    renderWithClient(
      <InstallmentsSubform
        {...base}
        rows={[row({ paymentMethod: 'Boleto', boletoDigitableLine: '123' })]}
        onRowsChange={onRowsChange}
      />,
    );
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Pix' } });
    expect(onRowsChange).toHaveBeenCalledWith([
      expect.objectContaining({ paymentMethod: 'Pix', boletoDigitableLine: '' }),
    ]);
  });

  it('esconde adicionar/redistribuir quando recorrente (Conta Fixa)', () => {
    renderWithClient(<InstallmentsSubform {...base} recurring rows={[row()]} onRowsChange={vi.fn()} />);
    expect(screen.queryByRole('button', { name: /adicionar parcela/i })).not.toBeInTheDocument();
  });

  it('não submete o <form> ao clicar nos botões do editor (type=button)', () => {
    const onSubmit = vi.fn((e: { preventDefault: () => void }) => e.preventDefault());
    renderWithClient(
      <form onSubmit={onSubmit}>
        <InstallmentsSubform {...base} rows={[row(), row({ dueDate: '2026-09-10' })]} onRowsChange={vi.fn()} />
      </form>,
    );
    fireEvent.click(screen.getByRole('button', { name: /distribuir igualmente/i }));
    fireEvent.click(screen.getByRole('button', { name: /adicionar parcela/i }));
    fireEvent.click(screen.getAllByRole('button', { name: /remover parcela/i })[0]);
    expect(onSubmit).not.toHaveBeenCalled();
  });
});
