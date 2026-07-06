/**
 * Tests for the WithholdingSection component:
 * (1) Editable mode computes Líquido = Bruto − Σ retenções live as rows change.
 * (2) Clicking Salvar calls api.setPayableWithholdings with the expected items payload.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

// ---------- mock react-router-dom ----------
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn(), useParams: () => ({}) };
});

// ---------- mock store ----------
vi.mock('../../store', async (orig) => {
  const actual = await orig<typeof import('../../store')>();
  const empresa = { id: 'company_1', legalName: 'ACME LTDA', cnpj: '00000000000191' };
  return {
    ...actual,
    useApp: () => ({
      empresa,
      empresaId: 'company_1',
      empresas: [empresa],
      setEmpresaId: vi.fn(),
    }),
  };
});

// ---------- capture mutate calls ----------
const mockMutate = vi.fn();

// ---------- mock queries ----------
vi.mock('../../queries', async (orig) => {
  const actual = await orig<typeof import('../../queries')>();
  return {
    ...actual,
    useSetPayableWithholdings: () => ({
      mutate: mockMutate,
      isPending: false,
    }),
  };
});

import { WithholdingSection } from './withholding-section';
import type { Payable } from '../../api';

// ---------- minimal Payable fixture ----------
function makePayable(overrides: Partial<Payable> = {}): Payable {
  return {
    id: 'payable_1',
    companyId: 'company_1',
    origin: 'Manual',
    vendorId: 'vendor_1',
    counterpartyName: 'Fornecedor Teste',
    documentNumber: null,
    issueDate: null,
    description: 'Serviços de TI',
    costCenterId: 'cost_center_1',
    categoryId: null,
    totalAmount: 1000,
    status: 'PendingApproval',
    rejectionReason: null,
    cancelReason: null,
    approvedBy: null,
    approvedByName: null,
    approvedAt: null,
    rejectedBy: null,
    rejectedByName: null,
    rejectedAt: null,
    accountingAccountId: null,
    financialAccountId: null,
    isClassified: false,
    suggestedAccountingAccountId: null,
    suggestedFinancialAccountId: null,
    classificationConfidence: null,
    classificationRationale: null,
    paymentBankAccountId: null,
    paymentMethod: 'Pix',
    createdAt: '2025-06-01T00:00:00Z',
    installments: [],
    attachments: [],
    withholdings: [],
    totalWithheld: 0,
    netTotal: 1000,
    ...overrides,
  };
}

describe('WithholdingSection — editable', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('computes Líquido = Bruto − Σ retenções as rows change', () => {
    const payable = makePayable({ totalAmount: 1000 });

    renderWithProviders(<WithholdingSection payable={payable} editable />);

    // Add a row via the "Adicionar retenção" button
    const addBtn = screen.getByText('Adicionar retenção');
    fireEvent.click(addBtn);

    // Row inputs are now base (index 0) + rate% (index 1); the withheld value is derived (locked, no input).
    const allInputs = screen.getAllByRole('textbox');
    fireEvent.change(allInputs[0], { target: { value: '1000' } }); // base
    fireEvent.change(allInputs[1], { target: { value: '15' } }); // rate %

    // amount = 1000 × 15% = 150 → net = 1000 − 150 = 850
    const liquidoEl = screen.getByTestId('withholding-liquido');
    // brlCents(850) in pt-BR locale → contains "850"
    expect(liquidoEl.textContent).toContain('850');
  });

  it('clicking Salvar calls useSetPayableWithholdings.mutate with the expected items', () => {
    const payable = makePayable({ totalAmount: 1000 });

    renderWithProviders(<WithholdingSection payable={payable} editable />);

    // Add a row
    fireEvent.click(screen.getByText('Adicionar retenção'));

    // Fill in base + rate (%); the withheld value is computed (base × rate), not typed.
    const allInputs = screen.getAllByRole('textbox');
    fireEvent.change(allInputs[0], { target: { value: '1000' } });
    fireEvent.change(allInputs[1], { target: { value: '15' } });

    // Click Salvar
    fireEvent.click(screen.getByText('Salvar retenções'));

    // rate is sent as a decimal fraction (15% → 0.15); amount is computed (1000 × 0.15 = 150).
    expect(mockMutate).toHaveBeenCalledWith({
      id: 'payable_1',
      items: [{ taxType: 'Irrf', baseAmount: 1000, rate: 0.15, amount: 150 }],
    });
  });
});

describe('WithholdingSection — read-only', () => {
  it('shows Bruto/Retido/Líquido from server values, no inputs', () => {
    const payable = makePayable({
      totalAmount: 2000,
      totalWithheld: 300,
      netTotal: 1700,
      withholdings: [{ id: 'wh_1', taxType: 'Irrf', baseAmount: 2000, rate: 0.15, amount: 300 }],
    });

    renderWithProviders(<WithholdingSection payable={payable} editable={false} />);

    // Should show IRRF label
    expect(screen.getByText('IRRF')).toBeTruthy();

    // Líquido from server (netTotal 1700) should appear — pin the actual value
    const liquidoEl = screen.getByTestId('withholding-liquido');
    expect(liquidoEl.textContent).toContain('1.700');

    // No editable controls
    expect(screen.queryByText('Adicionar retenção')).toBeNull();
    expect(screen.queryByText('Salvar retenções')).toBeNull();
  });
});
