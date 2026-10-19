import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CurrencyInput, Field, Input, MaskedInput, ReadonlyField, Select, Textarea, ToolbarField } from './ui';

/**
 * O rótulo tem que pertencer ao campo. Antes destes testes, `Field`/`ToolbarField` desenhavam
 * `<label>` e controle como irmãos sem `htmlFor` — clicar no rótulo não focava nada (a maior
 * concentração de dead clicks do Clarity) e leitor de tela não anunciava o campo.
 */
describe('Field', () => {
  it('associa o rótulo ao Input que envolve', () => {
    render(
      <Field label='Fornecedor'>
        <Input defaultValue='Papelaria Central' />
      </Field>,
    );

    expect(screen.getByLabelText('Fornecedor')).toHaveValue('Papelaria Central');
  });

  it('foca o Input quando o usuário clica no rótulo', async () => {
    const user = userEvent.setup();
    render(
      <Field label='Fornecedor'>
        <Input />
      </Field>,
    );

    await user.click(screen.getByText('Fornecedor'));

    expect(screen.getByLabelText('Fornecedor')).toHaveFocus();
  });

  it('deixa o call site fixar o id do campo via htmlFor', () => {
    render(
      <Field label='Fornecedor' htmlFor='vendor-custom'>
        <Input />
      </Field>,
    );

    expect(screen.getByLabelText('Fornecedor')).toHaveAttribute('id', 'vendor-custom');
  });

  it('não sobrescreve o id explícito de um controle', () => {
    render(
      <Field label='Fornecedor'>
        <Input id='vendor-custom' />
      </Field>,
    );

    expect(document.querySelector('input')).toHaveAttribute('id', 'vendor-custom');
  });

  it('associa o rótulo ao Select', () => {
    render(
      <Field label='Tipo'>
        <Select defaultValue='servico'>
          <option value='servico'>Serviço</option>
        </Select>
      </Field>,
    );

    expect(screen.getByLabelText('Tipo')).toHaveValue('servico');
  });

  it('associa o rótulo ao Textarea', () => {
    render(
      <Field label='Justificativa'>
        <Textarea defaultValue='troca de fornecedor' />
      </Field>,
    );

    expect(screen.getByLabelText('Justificativa')).toHaveValue('troca de fornecedor');
  });

  it('associa o rótulo mesmo com a marca de obrigatório', () => {
    render(
      <Field label='Fornecedor' required>
        <Input />
      </Field>,
    );

    expect(screen.getByLabelText(/Fornecedor/)).toBeInTheDocument();
  });

  it('associa o rótulo ao CurrencyInput', () => {
    render(
      <Field label='Valor estimado'>
        <CurrencyInput value='4.280,00' onValueChange={vi.fn()} />
      </Field>,
    );

    expect(screen.getByLabelText('Valor estimado')).toHaveValue('4.280,00');
  });

  it('associa o rótulo ao MaskedInput', () => {
    render(
      <Field label='CEP'>
        <MaskedInput value='92425900' mask={(raw) => raw} onValueChange={vi.fn()} />
      </Field>,
    );

    expect(screen.getByLabelText('CEP')).toHaveValue('92425900');
  });

  it('dá ids distintos a dois Fields com o mesmo rótulo', () => {
    render(
      <>
        <Field label='Valor'>
          <Input defaultValue='10' />
        </Field>
        <Field label='Valor'>
          <Input defaultValue='20' />
        </Field>
      </>,
    );

    const [first, second] = screen.getAllByLabelText('Valor');
    expect(first.id).not.toBe('');
    expect(first.id).not.toBe(second.id);
  });
});

describe('ToolbarField', () => {
  it('associa o rótulo ao controle que envolve', () => {
    render(
      <ToolbarField label='Método'>
        <Select defaultValue='pix'>
          <option value='pix'>PIX</option>
        </Select>
      </ToolbarField>,
    );

    expect(screen.getByLabelText('Método')).toHaveValue('pix');
  });

  it('foca o controle quando o usuário clica no rótulo', async () => {
    const user = userEvent.setup();
    render(
      <ToolbarField label='De'>
        <Input type='date' />
      </ToolbarField>,
    );

    await user.click(screen.getByText('De'));

    expect(screen.getByLabelText('De')).toHaveFocus();
  });
});

describe('ReadonlyField', () => {
  it('associa o rótulo ao valor somente-leitura', () => {
    render(<ReadonlyField label='Código' value='#2418' />);

    expect(screen.getByLabelText('Código')).toHaveValue('#2418');
  });
});
