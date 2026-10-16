import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';
import { setViewportMatches } from '../../test/setup';

const { mockUsePayables, mockUsePayable, mockHoldMutate, mockReleaseMutate } = vi.hoisted(() => ({
  mockUsePayables: vi.fn(),
  mockUsePayable: vi.fn(),
  mockHoldMutate: vi.fn(),
  mockReleaseMutate: vi.fn(),
}));
vi.mock('../../queries', () => ({
  usePayables: mockUsePayables,
  usePayable: mockUsePayable,
  useHoldInstallment: () => ({ mutate: mockHoldMutate }),
  useReleaseInstallmentHold: () => ({ mutate: mockReleaseMutate }),
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
}));

import { PaymentTab } from './payment-tab';

/** Base list item fields (matches PayableListItemResponse with rail fields from ListPayables). */
const baseItem = {
  payableId: 'payable_1',
  installmentId: 'i1',
  sequence: 1,
  installmentCount: 1,
  counterpartyName: 'ACME',
  description: 'NF 1',
  costCenterId: 'cc_1',
  categoryId: null,
  dueDate: '2026-06-20',
  amount: 800,
  netAmount: 750, // 50 withheld — the esteira pays (and sums) the net
  installmentStatus: 'Open',
  payableStatus: 'Scheduled',
  scheduledPaymentDate: '2026-07-01',
  isHeld: false,
  holdReason: null,
  remittanceId: null,
  remittanceSequence: null,
  rail: 'Remittance',
  manualReason: null,
};

/** A remittance-rail item (future scheduledPaymentDate, not held, rail='Remittance'). */
const remittanceItem = {
  ...baseItem,
  installmentId: 'i_rem',
  counterpartyName: 'ACME Remessa',
  scheduledPaymentDate: '2099-12-31', // far future → 'scheduled' bucket
  isHeld: false,
  rail: 'Remittance',
  manualReason: null,
};

/** A held item (isHeld=true → 'held' bucket → Manuais rail). */
const heldItem = {
  ...baseItem,
  installmentId: 'i_held',
  counterpartyName: 'Held Vendor',
  isHeld: true,
  holdReason: 'Disputa fiscal',
  scheduledPaymentDate: '2026-06-20',
  rail: 'Remittance',
  manualReason: null,
};

/** An InRemittance item. */
const inRemittanceItem = {
  ...baseItem,
  installmentId: 'i_inrem',
  counterpartyName: 'Remessa Vendor',
  installmentStatus: 'InRemittance',
  remittanceId: 'remittance_abc123',
  remittanceSequence: 3,
  rail: 'Remittance',
  manualReason: null,
};

/** A manual item (vendor has no bank info → rail='Manual', manualReason='VendorBankInfoMissing'). */
const manualItem = {
  ...baseItem,
  installmentId: 'i_manual',
  counterpartyName: 'Manual Vendor',
  isHeld: false,
  rail: 'Manual',
  manualReason: 'VendorBankInfoMissing',
};

beforeEach(() => {
  mockUsePayable.mockReturnValue({ data: undefined });
  mockUsePayables.mockReturnValue({ data: { items: [baseItem], total: 1, totalPages: 1 } });
});

describe('PaymentTab', () => {
  it('queries Scheduled payables and renders a period group with the vendor + amount', () => {
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(mockUsePayables).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'Scheduled' }),
      expect.anything(),
    );
    expect(screen.getByText('ACME')).toBeInTheDocument();
    expect(screen.getAllByText(/R\$/).length).toBeGreaterThan(0);
    expect(screen.getByText(/A pagar:/)).toBeInTheDocument();
  });

  it('shows gross and net ("A pagar") per line and sums the net everywhere', () => {
    mockUsePayables.mockReturnValue({ data: { items: [remittanceItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    // Two amount columns on the rail table.
    expect(screen.getByText('Valor bruto')).toBeInTheDocument();
    expect(screen.getAllByText('A pagar').length).toBeGreaterThan(0);
    // The line shows the gross (800) AND the net (750); the summary + panel subtotal sum the net.
    expect(screen.getAllByText(/R\$\s?800/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/R\$\s?750/).length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText(/A pagar:\s*R\$\s?800/)).toBeNull();
  });

  it('renders two sub-tabs Remessas and Manuais', () => {
    mockUsePayables.mockReturnValue({ data: { items: [remittanceItem, heldItem], total: 2, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(screen.getByRole('tab', { name: /Remessas/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Manuais/ })).toBeInTheDocument();
  });

  it('remittance-rail future item appears under Remessas with Agendada chip and Segurar button', () => {
    mockUsePayables.mockReturnValue({ data: { items: [remittanceItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    // Remessas tab is active by default
    expect(screen.getByRole('tab', { name: /Remessas/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('ACME Remessa')).toBeInTheDocument();
    // 'Agendada' chip for scheduled bucket
    expect(screen.getByText(/Agendada/)).toBeInTheDocument();
    // Segurar button
    expect(screen.getByRole('button', { name: /Segurar/ })).toBeInTheDocument();
  });

  it('clicking Segurar calls useHoldInstallment with a reason', async () => {
    mockUsePayables.mockReturnValue({ data: { items: [remittanceItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: /Segurar/ }));
    // Reason prompt appears
    const reasonInput = await screen.findByPlaceholderText(/Motivo/);
    fireEvent.change(reasonInput, { target: { value: 'Aguardando NF' } });
    // Confirm hold
    fireEvent.click(screen.getByRole('button', { name: /Confirmar/ }));
    await waitFor(() =>
      expect(mockHoldMutate).toHaveBeenCalledWith(
        expect.objectContaining({
          id: remittanceItem.payableId,
          installmentId: remittanceItem.installmentId,
          reason: 'Aguardando NF',
        }),
      ),
    );
  });

  it('held item appears under Manuais (Em espera group) with Liberar button', () => {
    mockUsePayables.mockReturnValue({ data: { items: [heldItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    // Switch to Manuais tab
    fireEvent.click(screen.getByRole('tab', { name: /Manuais/ }));
    expect(screen.getByText('Held Vendor')).toBeInTheDocument();
    // Em espera appears as panel title and as the status chip — both use the same i18n key
    expect(screen.getAllByText(/Em espera/).length).toBeGreaterThanOrEqual(1);
    // Liberar button
    expect(screen.getByRole('button', { name: /Liberar/ })).toBeInTheDocument();
  });

  it('clicking Liberar calls useReleaseInstallmentHold', () => {
    mockUsePayables.mockReturnValue({ data: { items: [heldItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('tab', { name: /Manuais/ }));
    fireEvent.click(screen.getByRole('button', { name: /Liberar/ }));
    expect(mockReleaseMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        id: heldItem.payableId,
        installmentId: heldItem.installmentId,
      }),
    );
  });

  it('InRemittance item shows clickable remittance chip under Remessas', () => {
    mockUsePayables.mockReturnValue({ data: { items: [inRemittanceItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    // Should appear in Remessas rail (inRemittance bucket)
    expect(screen.getByText('Remessa Vendor')).toBeInTheDocument();
    // Chip with Remessa #N text
    expect(screen.getByText(/Remessa #3/)).toBeInTheDocument();
  });

  it('a rail=Manual item (vendor lacking bank info) appears under Manuais with Dar baixa button', () => {
    // manualItem has rail='Manual' (VendorBankInfoMissing) — must appear in Manuais, not Remessas.
    mockUsePayables.mockReturnValue({ data: { items: [manualItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    // Switch to Manuais tab — manualItem must be there
    fireEvent.click(screen.getByRole('tab', { name: /Manuais/ }));
    expect(screen.getByText('Manual Vendor')).toBeInTheDocument();
    // Dar baixa button available for non-held manual items
    expect(screen.getByRole('button', { name: /Baixa|baixa|Registrar|registrar/i })).toBeInTheDocument();
  });

  it('rail=Remittance item appears under Remessas, not under Manuais', () => {
    mockUsePayables.mockReturnValue({ data: { items: [remittanceItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    // Remessas tab is default — item must be there
    expect(screen.getByText('ACME Remessa')).toBeInTheDocument();
    // Switch to Manuais — item must NOT be there
    fireEvent.click(screen.getByRole('tab', { name: /Manuais/ }));
    expect(screen.queryByText('ACME Remessa')).not.toBeInTheDocument();
  });
});

describe('PaymentTab no telefone', () => {
  beforeEach(() => setViewportMatches(true));

  it('mostra favorecido e "a pagar" no cabeçalho do card, sem <table>', () => {
    mockUsePayables.mockReturnValue({ data: { items: [remittanceItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(screen.getByText('ACME Remessa')).toBeInTheDocument();
    expect(screen.getAllByText(/R\$\s?750/).length).toBeGreaterThan(0);
    expect(document.querySelector('table')).not.toBeInTheDocument();
  });

  it('revela o valor bruto ao expandir', async () => {
    mockUsePayables.mockReturnValue({ data: { items: [remittanceItem], total: 1, totalPages: 1 } });
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(screen.queryByText(/R\$\s?800/)).not.toBeInTheDocument();
    await userEvent.click(screen.getAllByRole('button', { name: /expandir/i })[0]);
    expect(screen.getByText(/R\$\s?800/)).toBeInTheDocument();
  });
});
