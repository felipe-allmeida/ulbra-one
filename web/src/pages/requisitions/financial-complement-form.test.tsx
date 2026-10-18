import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import '../../i18n';
import { type Requisition } from '../../api';

const { mockSubmit, mockUpload } = vi.hoisted(() => ({ mockSubmit: vi.fn(), mockUpload: vi.fn() }));

vi.mock('../../queries', () => ({
  useSubmitFinancialComplement: () => ({ mutate: mockSubmit, isPending: false }),
  useRegisterRequisitionAttachment: () => ({ mutate: mockUpload, isPending: false }),
  // Boleto rows embed the reader — stub its hooks (no file is selected in these tests).
  useRequestBoletoUploadUrl: () => ({ mutateAsync: vi.fn(), reset: vi.fn(), isPending: false }),
  useParseBoleto: () => ({ mutateAsync: vi.fn(), reset: vi.fn(), isPending: false }),
  uploadToPresignedUrl: vi.fn(),
  useVendor: () => ({ data: undefined }),
}));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

import { FinancialComplementForm } from './financial-complement-form';

// Only the fields the form reads; the rest of Requisition is irrelevant here.
const baseReq = {
  id: 'req_1',
  status: 'FinancialComplement',
  estimateValue: 1000,
  firstDueDate: '2026-07-10',
  paymentInstallmentsCount: 1,
  paymentFrequency: null,
  installments: [],
  issueDate: null,
} as unknown as Requisition;

describe('FinancialComplementForm', () => {
  beforeEach(() => {
    mockSubmit.mockReset();
    mockUpload.mockReset();
  });

  it('seeds a single installment and shows the billing summary', () => {
    render(<FinancialComplementForm req={baseReq} />);
    expect(screen.getByText('Aprovado')).toBeInTheDocument();
    expect(screen.getByText('Programado')).toBeInTheDocument();
    expect(screen.getByText('Diferença')).toBeInTheDocument();
    // The single seeded parcela carries the full estimate value (canonical pt-BR).
    expect(screen.getByDisplayValue('1.000,00')).toBeInTheDocument();
  });

  it('submits the authored installments + emission date', () => {
    render(<FinancialComplementForm req={baseReq} />);
    fireEvent.change(screen.getByLabelText('Data de emissão'), { target: { value: '2026-07-05' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar informações financeiras' }));

    expect(mockSubmit).toHaveBeenCalledTimes(1);
    const [vars] = mockSubmit.mock.calls[0];
    expect(vars).toMatchObject({ id: 'req_1', issueDate: '2026-07-05' });
    expect(vars.installments).toHaveLength(1);
    expect(vars.installments[0]).toMatchObject({ dueDate: '2026-07-10', amount: 1000, paymentMethod: 'Pix' });
  });

  it('blocks submit without an emission date and surfaces the field error', () => {
    render(<FinancialComplementForm req={baseReq} />);
    fireEvent.click(screen.getByRole('button', { name: 'Enviar informações financeiras' }));

    expect(mockSubmit).not.toHaveBeenCalled();
    expect(screen.getByText('Informe a data de emissão.')).toBeInTheDocument();
  });

  it('mostra o campo de boleto só quando a forma é Boleto e limpa ao trocar de volta', () => {
    render(<FinancialComplementForm req={baseReq} />);
    expect(screen.queryByLabelText('Linha digitável do boleto')).not.toBeInTheDocument();
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Boleto' } });
    expect(screen.getByLabelText('Linha digitável do boleto')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Pix' } });
    expect(screen.queryByLabelText('Linha digitável do boleto')).not.toBeInTheDocument();
  });

  it('sinaliza convênio quando a linha digitável começa com 8', () => {
    render(<FinancialComplementForm req={baseReq} />);
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Boleto' } });
    const input = screen.getByLabelText('Linha digitável do boleto');
    fireEvent.change(input, { target: { value: '23793381286008271309005678911101234560000010000' } }); // bancário (47)
    expect(screen.queryByText('Convênio')).not.toBeInTheDocument();
    fireEvent.change(input, { target: { value: '846700000017599200241114202410000000000000000000' } }); // convênio (48, começa com 8)
    expect(screen.getByText('Convênio')).toBeInTheDocument();
  });

  it('envia a linha digitável só na parcela paga por Boleto', () => {
    render(<FinancialComplementForm req={baseReq} />);
    fireEvent.change(screen.getByLabelText('Data de emissão'), { target: { value: '2026-07-05' } });
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Boleto' } });
    fireEvent.change(screen.getByLabelText('Linha digitável do boleto'), {
      target: { value: '23793381286008271309005678911101234560000010000' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar informações financeiras' }));

    expect(mockSubmit).toHaveBeenCalledTimes(1);
    const [vars] = mockSubmit.mock.calls[0];
    expect(vars.installments[0]).toMatchObject({
      paymentMethod: 'Boleto',
      boletoDigitableLine: '23793381286008271309005678911101234560000010000',
    });
  });

  it('bloqueia o envio quando o boleto tem tamanho inválido', () => {
    render(<FinancialComplementForm req={baseReq} />);
    fireEvent.change(screen.getByLabelText('Data de emissão'), { target: { value: '2026-07-05' } });
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Boleto' } });
    fireEvent.change(screen.getByLabelText('Linha digitável do boleto'), { target: { value: '12345' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar informações financeiras' }));

    expect(mockSubmit).not.toHaveBeenCalled();
    expect(
      screen.getByText('Informe a linha digitável do boleto (47 bancário ou 48 convênio dígitos).'),
    ).toBeInTheDocument();
  });

  it('bloqueia o envio quando a parcela é Boleto e a linha digitável está em branco (AEL-932)', () => {
    render(<FinancialComplementForm req={baseReq} />);
    fireEvent.change(screen.getByLabelText('Data de emissão'), { target: { value: '2026-07-05' } });
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Boleto' } });
    // No boleto line typed at all.
    fireEvent.click(screen.getByRole('button', { name: 'Enviar informações financeiras' }));

    expect(mockSubmit).not.toHaveBeenCalled();
    expect(
      screen.getByText('Informe a linha digitável do boleto (47 bancário ou 48 convênio dígitos).'),
    ).toBeInTheDocument();
  });
});
