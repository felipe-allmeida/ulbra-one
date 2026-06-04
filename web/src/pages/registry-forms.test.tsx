import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ToastProvider } from '../toast';
import { VendorForm } from './registry-forms';
import { api } from '../api';

vi.mock('../api', async (orig) => {
  const actual = await orig<typeof import('../api')>();
  return {
    ...actual,
    api: {
      lookupCnpj: vi.fn(),
      lookupCep: vi.fn(),
      createVendor: vi.fn(),
    },
  };
});

const renderForm = () =>
  render(
    <ToastProvider>
      <VendorForm companyId="c1" onSaved={() => {}} onCancel={() => {}} />
    </ToastProvider>,
  );

describe('VendorForm', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows an inline error and disables save for an invalid CNPJ', () => {
    renderForm();
    const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
    fireEvent.change(docInput, { target: { value: '11.111.111/1111-11' } });

    expect(screen.getByText('CNPJ inválido.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Criar' })).toBeDisabled();
  });

  it('auto-fills name/tradeName/address from a mocked CNPJ lookup', async () => {
    (api.lookupCnpj as ReturnType<typeof vi.fn>).mockResolvedValue({
      razaoSocial: 'ACME LTDA',
      nomeFantasia: 'Acme',
      address: { cep: '01001000', street: 'Praça da Sé', number: '1', neighborhood: 'Sé', city: 'São Paulo', state: 'SP' },
    });

    renderForm();
    const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
    fireEvent.change(docInput, { target: { value: '11222333000181' } });
    fireEvent.blur(docInput);

    await waitFor(() => {
      expect(api.lookupCnpj).toHaveBeenCalledWith('11222333000181');
      expect(screen.getByDisplayValue('ACME LTDA')).toBeInTheDocument();
      expect(screen.getByDisplayValue('Acme')).toBeInTheDocument();
      expect(screen.getByDisplayValue('Praça da Sé')).toBeInTheDocument();
    });
  });
});
