import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
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

const renderForm = () => renderWithProviders(<VendorForm companyId='c1' onSaved={() => {}} onCancel={() => {}} />);

describe('VendorForm', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows an inline error and disables save for an invalid CNPJ', async () => {
    renderForm();
    const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
    fireEvent.change(docInput, { target: { value: '11.111.111/1111-11' } });

    await waitFor(() => expect(screen.getByText('CNPJ inválido.')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Criar' })).toBeDisabled();
    expect(api.createVendor).not.toHaveBeenCalled();
  });

  it('does not call the mutation when submitting an invalid document (inline error only)', async () => {
    renderForm();
    const form = screen.getByRole('button', { name: 'Criar' }).closest('form')!;
    fireEvent.change(screen.getByPlaceholderText('00.000.000/0000-00'), { target: { value: '11.111.111/1111-11' } });
    fireEvent.submit(form);

    await waitFor(() => expect(screen.getByText('CNPJ inválido.')).toBeInTheDocument());
    expect(api.createVendor).not.toHaveBeenCalled();
  });

  it('submits a valid form and calls the create mutation', async () => {
    // Use the CNPJ auto-fill to populate the required name, then submit.
    (api.lookupCnpj as ReturnType<typeof vi.fn>).mockResolvedValue({
      razaoSocial: 'ACME LTDA',
      nomeFantasia: null,
      address: null,
    });
    (api.createVendor as ReturnType<typeof vi.fn>).mockResolvedValue({ id: 'v1', companyId: 'c1' });
    renderForm();

    const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
    fireEvent.change(docInput, { target: { value: '11222333000181' } });
    fireEvent.blur(docInput);
    await waitFor(() => expect(screen.getByDisplayValue('ACME LTDA')).toBeInTheDocument());

    fireEvent.submit(screen.getByRole('button', { name: 'Criar' }).closest('form')!);

    await waitFor(() => expect(api.createVendor).toHaveBeenCalledTimes(1));
    const body = (api.createVendor as ReturnType<typeof vi.fn>).mock.calls[0][0];
    expect(body).toMatchObject({
      companyId: 'c1',
      name: 'ACME LTDA',
      document: '11222333000181',
      personType: 'Company',
    });
  });

  it('auto-fills name/tradeName/address from a mocked CNPJ lookup', async () => {
    (api.lookupCnpj as ReturnType<typeof vi.fn>).mockResolvedValue({
      razaoSocial: 'ACME LTDA',
      nomeFantasia: 'Acme',
      address: {
        cep: '01001000',
        street: 'Praça da Sé',
        number: '1',
        neighborhood: 'Sé',
        city: 'São Paulo',
        state: 'SP',
      },
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
