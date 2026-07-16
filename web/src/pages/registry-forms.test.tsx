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

// FormActions esconde Salvar/Criar sem registry:write; força a permissão aqui (o form é o alvo do teste).
vi.mock('../permissions', async (orig) => ({
  ...(await orig<typeof import('../permissions')>()),
  usePermissions: () => ({ can: () => true, canAny: () => true, me: null, isAdmin: true }),
}));

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

  it('renders structured bank fields and tax-regime select', () => {
    renderForm();

    // Structured bank inputs
    expect(screen.getByPlaceholderText('ex.: 001')).toBeInTheDocument(); // bankCode
    expect(screen.getByPlaceholderText('ex.: 0001')).toBeInTheDocument(); // branch
    expect(screen.getByPlaceholderText('ex.: 12345')).toBeInTheDocument(); // account
    expect(screen.getByPlaceholderText('ex.: 0')).toBeInTheDocument(); // accountDigit
    expect(screen.getByPlaceholderText('CPF, CNPJ, e-mail, telefone ou chave aleatória')).toBeInTheDocument(); // pixKey

    // Tax regime select with pt-BR option labels
    const select = screen.getByDisplayValue('Não informado'); // default = Unknown
    expect(select).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'MEI' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Simples Nacional' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Lucro Presumido' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Lucro Real' })).toBeInTheDocument();
  });

  it('submits structured bankInfo and taxRegime in the mutation payload', async () => {
    (api.lookupCnpj as ReturnType<typeof vi.fn>).mockResolvedValue({
      razaoSocial: 'TECH LTDA',
      nomeFantasia: null,
      address: null,
    });
    (api.createVendor as ReturnType<typeof vi.fn>).mockResolvedValue({ id: 'v2', companyId: 'c1' });
    renderForm();

    // Fill CNPJ and wait for auto-fill
    const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
    fireEvent.change(docInput, { target: { value: '11222333000181' } });
    fireEvent.blur(docInput);
    await waitFor(() => expect(screen.getByDisplayValue('TECH LTDA')).toBeInTheDocument());

    // Fill structured bank fields
    fireEvent.change(screen.getByPlaceholderText('ex.: 001'), { target: { value: '341' } });
    fireEvent.change(screen.getByPlaceholderText('ex.: 0001'), { target: { value: '0001' } });
    fireEvent.change(screen.getByPlaceholderText('ex.: 12345'), { target: { value: '98765' } });
    fireEvent.change(screen.getByPlaceholderText('ex.: 0'), { target: { value: '4' } });
    fireEvent.change(screen.getByPlaceholderText('CPF, CNPJ, e-mail, telefone ou chave aleatória'), {
      target: { value: 'pagamentos@tech.com' },
    });

    // Change tax regime
    fireEvent.change(screen.getByDisplayValue('Não informado'), { target: { value: 'SimplesNacional' } });

    // Submit
    fireEvent.submit(screen.getByRole('button', { name: 'Criar' }).closest('form')!);

    await waitFor(() => expect(api.createVendor).toHaveBeenCalledTimes(1));
    const body = (api.createVendor as ReturnType<typeof vi.fn>).mock.calls[0][0];
    expect(body).toMatchObject({
      companyId: 'c1',
      name: 'TECH LTDA',
      taxRegime: 'SimplesNacional',
      bankInfo: {
        bankCode: '341',
        branch: '0001',
        account: '98765',
        accountDigit: '4',
        pixKey: 'pagamentos@tech.com',
      },
    });
  });

  describe('payment schedule', () => {
    it('renders the Política de pagamento select with default OnDueDate', () => {
      renderForm();
      // The select is rendered with the default value
      const select = screen.getByDisplayValue('No vencimento');
      expect(select).toBeInTheDocument();
      // All three options are present
      expect(screen.getByRole('option', { name: 'No vencimento' })).toBeInTheDocument();
      expect(screen.getByRole('option', { name: 'Dia fixo do mês' })).toBeInTheDocument();
      expect(screen.getByRole('option', { name: 'Primeiro dia do mês' })).toBeInTheDocument();
    });

    it('hides day input when mode is OnDueDate (default)', () => {
      renderForm();
      // No number input for day shown in default mode
      expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
    });

    it('reveals day input when mode is FixedDayOfMonth', async () => {
      renderForm();
      const select = screen.getByDisplayValue('No vencimento');
      fireEvent.change(select, { target: { value: 'FixedDayOfMonth' } });
      await waitFor(() => expect(screen.getByRole('spinbutton')).toBeInTheDocument());
    });

    it('hides day input again when mode changes back from FixedDayOfMonth', async () => {
      renderForm();
      const select = screen.getByDisplayValue('No vencimento');
      fireEvent.change(select, { target: { value: 'FixedDayOfMonth' } });
      await waitFor(() => expect(screen.getByRole('spinbutton')).toBeInTheDocument());
      fireEvent.change(select, { target: { value: 'FirstDayOfMonth' } });
      await waitFor(() => expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument());
    });

    it('submits paymentScheduleMode=OnDueDate and paymentScheduleDay=null by default', async () => {
      (api.lookupCnpj as ReturnType<typeof vi.fn>).mockResolvedValue({
        razaoSocial: 'SCHED LTDA',
        nomeFantasia: null,
        address: null,
      });
      (api.createVendor as ReturnType<typeof vi.fn>).mockResolvedValue({ id: 'v3', companyId: 'c1' });
      renderForm();

      const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
      fireEvent.change(docInput, { target: { value: '11222333000181' } });
      fireEvent.blur(docInput);
      await waitFor(() => expect(screen.getByDisplayValue('SCHED LTDA')).toBeInTheDocument());

      fireEvent.submit(screen.getByRole('button', { name: 'Criar' }).closest('form')!);
      await waitFor(() => expect(api.createVendor).toHaveBeenCalledTimes(1));

      const body = (api.createVendor as ReturnType<typeof vi.fn>).mock.calls[0][0];
      expect(body).toMatchObject({
        paymentScheduleMode: 'OnDueDate',
        paymentScheduleDay: null,
      });
    });

    it('submits paymentScheduleMode=FixedDayOfMonth with paymentScheduleDay in payload', async () => {
      (api.lookupCnpj as ReturnType<typeof vi.fn>).mockResolvedValue({
        razaoSocial: 'FIXDAY LTDA',
        nomeFantasia: null,
        address: null,
      });
      (api.createVendor as ReturnType<typeof vi.fn>).mockResolvedValue({ id: 'v4', companyId: 'c1' });
      renderForm();

      const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
      fireEvent.change(docInput, { target: { value: '11222333000181' } });
      fireEvent.blur(docInput);
      await waitFor(() => expect(screen.getByDisplayValue('FIXDAY LTDA')).toBeInTheDocument());

      // Change to FixedDayOfMonth
      const select = screen.getByDisplayValue('No vencimento');
      fireEvent.change(select, { target: { value: 'FixedDayOfMonth' } });
      await waitFor(() => expect(screen.getByRole('spinbutton')).toBeInTheDocument());

      // Fill the day input
      const dayInput = screen.getByRole('spinbutton');
      fireEvent.change(dayInput, { target: { value: '15' } });

      fireEvent.submit(screen.getByRole('button', { name: 'Criar' }).closest('form')!);
      await waitFor(() => expect(api.createVendor).toHaveBeenCalledTimes(1));

      const body = (api.createVendor as ReturnType<typeof vi.fn>).mock.calls[0][0];
      expect(body).toMatchObject({
        paymentScheduleMode: 'FixedDayOfMonth',
        paymentScheduleDay: 15,
      });
    });

    it('blocks submit when FixedDayOfMonth and day is missing', async () => {
      (api.lookupCnpj as ReturnType<typeof vi.fn>).mockResolvedValue({
        razaoSocial: 'NODAY LTDA',
        nomeFantasia: null,
        address: null,
      });
      (api.createVendor as ReturnType<typeof vi.fn>).mockResolvedValue({ id: 'v5', companyId: 'c1' });
      renderForm();

      const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
      fireEvent.change(docInput, { target: { value: '11222333000181' } });
      fireEvent.blur(docInput);
      await waitFor(() => expect(screen.getByDisplayValue('NODAY LTDA')).toBeInTheDocument());

      // Change to FixedDayOfMonth but don't fill day
      const select = screen.getByDisplayValue('No vencimento');
      fireEvent.change(select, { target: { value: 'FixedDayOfMonth' } });
      await waitFor(() => expect(screen.getByRole('spinbutton')).toBeInTheDocument());

      // Submit button should be disabled (form invalid due to missing day)
      await waitFor(() => expect(screen.getByRole('button', { name: 'Criar' })).toBeDisabled());
      expect(api.createVendor).not.toHaveBeenCalled();
    });

    it('blocks submit when FixedDayOfMonth and day is out of range', async () => {
      (api.lookupCnpj as ReturnType<typeof vi.fn>).mockResolvedValue({
        razaoSocial: 'RANGE LTDA',
        nomeFantasia: null,
        address: null,
      });
      renderForm();

      const docInput = screen.getByPlaceholderText('00.000.000/0000-00');
      fireEvent.change(docInput, { target: { value: '11222333000181' } });
      fireEvent.blur(docInput);
      await waitFor(() => expect(screen.getByDisplayValue('RANGE LTDA')).toBeInTheDocument());

      const select = screen.getByDisplayValue('No vencimento');
      fireEvent.change(select, { target: { value: 'FixedDayOfMonth' } });
      await waitFor(() => expect(screen.getByRole('spinbutton')).toBeInTheDocument());

      const dayInput = screen.getByRole('spinbutton');
      fireEvent.change(dayInput, { target: { value: '31' } });

      await waitFor(() => expect(screen.getByText('O dia deve ser entre 1 e 28.')).toBeInTheDocument());
      expect(screen.getByRole('button', { name: 'Criar' })).toBeDisabled();
    });

    it('hydrates paymentScheduleMode and paymentScheduleDay from an existing vendor (edit mode)', () => {
      const existingVendor = {
        id: 'v10',
        companyId: 'c1',
        personType: 'Company' as const,
        name: 'EDIT VENDOR LTDA',
        tradeName: null,
        document: '11222333000181',
        email: null,
        phone: null,
        address: null,
        paymentTerms: null,
        bankInfo: { bankCode: '', branch: '', account: '', accountDigit: '', pixKey: '' },
        taxRegime: 'Unknown' as const,
        cprbForeseen: null,
        simplesAnexo: null,
        paymentScheduleMode: 'FixedDayOfMonth' as const,
        paymentScheduleDay: 20,
        acceptedPaymentMethods: [],
        withholdingRates: [],
        active: true,
      };
      renderWithProviders(
        <VendorForm companyId='c1' initial={existingVendor} onSaved={() => {}} onCancel={() => {}} />,
      );

      // Mode should be hydrated
      expect(screen.getByDisplayValue('Dia fixo do mês')).toBeInTheDocument();
      // Day input should appear and be hydrated
      const dayInput = screen.getByRole<HTMLInputElement>('spinbutton');
      expect(dayInput.value).toBe('20');
    });
  });
});
