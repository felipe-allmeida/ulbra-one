import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { WithholdingRuleForm } from './withholding-rule-form';
import { api } from '../api';

vi.mock('../api', async (orig) => {
  const actual = await orig<typeof import('../api')>();
  return {
    ...actual,
    api: {
      listCompanies: vi.fn().mockResolvedValue([]),
      createWithholdingRule: vi.fn(),
      updateWithholdingRule: vi.fn(),
    },
  };
});

type Mock = ReturnType<typeof vi.fn>;
const create = () => api.createWithholdingRule as Mock;
const renderForm = () => renderWithProviders(<WithholdingRuleForm onSaved={() => {}} onCancel={() => {}} />);
const submit = () => fireEvent.submit(screen.getByRole('button', { name: 'Criar' }).closest('form')!);
const selectOf = (optionName: string) => screen.getByRole('option', { name: optionName }).closest('select')!;
const createdBody = () => create().mock.calls[0][0];

describe('WithholdingRuleForm', () => {
  beforeEach(() => vi.clearAllMocks());

  it('blocks submit and shows an inline error when a retaining Fixed rule has no rate', async () => {
    renderForm(); // defaults: IRRF, retém=Sim, origem=Fixa, alíquota vazia → inválido
    submit();

    await waitFor(() =>
      expect(screen.getByText('Informe uma alíquota positiva para retenção com alíquota fixa.')).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: 'Criar' })).toBeDisabled();
    expect(create()).not.toHaveBeenCalled();
  });

  it('submits an INSS CPRB rule, converting the percent to a fraction', async () => {
    create().mockResolvedValue({ id: 'w1' });
    renderForm();

    fireEvent.change(screen.getByDisplayValue('IRRF'), { target: { value: 'Inss' } }); // taxType
    fireEvent.change(screen.getByPlaceholderText('ex.: 11'), { target: { value: '3.5' } }); // rate (%)
    fireEvent.change(screen.getByDisplayValue('Desconhecido'), { target: { value: 'true' } }); // cprb → Sim
    submit();

    await waitFor(() => expect(create()).toHaveBeenCalledTimes(1));
    expect(createdBody()).toMatchObject({
      taxType: 'Inss',
      retains: true,
      rateSource: 'Fixed',
      rate: 0.035,
      cprb: true,
      simplesAnexo: null,
    });
  });

  it('reveals the Anexo field only for Simples and submits Anexo IV', async () => {
    create().mockResolvedValue({ id: 'w2' });
    renderForm();

    expect(screen.queryByText('Anexo do Simples')).not.toBeInTheDocument();
    fireEvent.change(screen.getByDisplayValue('Qualquer regime'), { target: { value: 'SimplesNacional' } });
    await waitFor(() => expect(screen.getByText('Anexo do Simples')).toBeInTheDocument());

    fireEvent.change(screen.getByPlaceholderText('ex.: 11'), { target: { value: '11' } });
    fireEvent.change(selectOf('Anexo IV'), { target: { value: 'IV' } });
    submit();

    await waitFor(() => expect(create()).toHaveBeenCalledTimes(1));
    expect(createdBody()).toMatchObject({
      vendorRegime: 'SimplesNacional',
      simplesAnexo: 'IV',
      rate: 0.11,
    });
  });

  it('forces reason=None when the rule retains', async () => {
    create().mockResolvedValue({ id: 'w3' });
    renderForm();

    expect(screen.queryByText('Motivo da não retenção')).not.toBeInTheDocument();
    fireEvent.change(screen.getByPlaceholderText('ex.: 11'), { target: { value: '1.5' } });
    submit();

    await waitFor(() => expect(create()).toHaveBeenCalledTimes(1));
    expect(createdBody().reason).toBe('None');
  });

  it('shows the reason (and hides the rate) when not retaining, and submits it with rate=null', async () => {
    create().mockResolvedValue({ id: 'w4' });
    renderForm();

    fireEvent.change(screen.getByDisplayValue('Sim'), { target: { value: 'false' } }); // retains → Não
    await waitFor(() => expect(screen.getByText('Motivo da não retenção')).toBeInTheDocument());
    expect(screen.queryByPlaceholderText('ex.: 11')).not.toBeInTheDocument();

    fireEvent.change(selectOf('Isenção por regime'), { target: { value: 'RegimeExemption' } });
    submit();

    await waitFor(() => expect(create()).toHaveBeenCalledTimes(1));
    expect(createdBody()).toMatchObject({ retains: false, reason: 'RegimeExemption', rate: null });
  });
});
