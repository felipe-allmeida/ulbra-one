import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUsePayables, mockUseSummary, mockUsePayable, permState } = vi.hoisted(() => ({
  mockUsePayables: vi.fn(),
  mockUseSummary: vi.fn(),
  mockUsePayable: vi.fn(),
  permState: {
    perms: new Set<string>(['payables:read', 'payables:write', 'payables:pay', 'treasury:read', 'treasury:write']),
  },
}));

vi.mock('../../queries', () => ({
  usePayables: mockUsePayables,
  usePaymentQueueSummary: mockUseSummary,
  usePayable: mockUsePayable,
  useHoldInstallment: () => ({ mutate: vi.fn() }),
  useReleaseInstallmentHold: () => ({ mutate: vi.fn() }),
  useUnits: () => ({ data: [{ id: 'unit_1', acronym: 'Ulbra S.A', name: 'Mantenedora' }] }),
  useBankAccounts: () => ({ data: [], isLoading: false }),
  useCreateRemittance: () => ({ mutate: vi.fn(), isPending: false }),
  useMe: () => ({ data: { isAdmin: false, permissions: [] } }),
}));
vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof import('../../permissions')>()),
  usePermissions: () => ({
    me: { isAdmin: false },
    isAdmin: false,
    can: (p: string) => permState.perms.has(p),
    canAny: (ps: readonly string[]) => ps.some((p) => permState.perms.has(p)),
  }),
}));

import { PaymentQueueTab } from './payment-queue-tab';

const iso = (offsetDays: number) => {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  return d.toISOString().slice(0, 10);
};
const today = iso(0);
const yesterday = iso(-1);
const inFive = iso(5);

const baseItem = {
  payableId: 'pay_1',
  installmentId: 'i1',
  sequence: 1,
  installmentCount: 1,
  counterpartyName: 'ACME',
  description: '',
  costCenterId: 'cc_1',
  categoryId: null,
  dueDate: inFive,
  amount: 800,
  netAmount: 750,
  installmentStatus: 'Open',
  payableStatus: 'Scheduled',
  scheduledPaymentDate: '2099-12-31', // → 'scheduled' bucket (selectable)
  isHeld: false,
  holdReason: null,
  remittanceId: null,
  remittanceSequence: null,
  rail: 'Remittance',
  manualReason: null,
  vendorDocument: '92802784000190',
  vendorId: 'ven_1',
  effectiveMethod: 'Boleto',
  boletoDigitableLine: null,
  documentNumber: '31854541',
  issueDate: `${today.slice(0, 7)}-01`,
  originUnitId: 'unit_1',
  hasFiscalDocument: true,
};

const overdueItem = {
  ...baseItem,
  installmentId: 'i_over',
  counterpartyName: 'Atrasada Ltda',
  dueDate: yesterday,
};
const manualItem = {
  ...baseItem,
  installmentId: 'i_manual',
  counterpartyName: 'Manual Vendor',
  rail: 'Manual',
  manualReason: 'VendorBankInfoMissing',
  effectiveMethod: 'Pix',
  hasFiscalDocument: false,
};
const agreementItem = {
  ...baseItem,
  installmentId: 'i_agree',
  counterpartyName: 'Convênio Vendor',
  effectiveMethod: 'Boleto',
  boletoDigitableLine: '8' + '4'.repeat(47),
};

const summary = {
  asOf: today,
  overdueCount: 73,
  overdueNetTotal: 311969.73,
  groups: [
    { dueDate: today, count: 5, netTotal: 5388.27 },
    { dueDate: inFive, count: 9, netTotal: 1234.56 },
  ],
  totalCount: 87,
  totalNetTotal: 318592.56,
};

const renderTab = () =>
  render(
    <MemoryRouter>
      <PaymentQueueTab companyId='company_1' search='' />
    </MemoryRouter>,
  );

const clipboardWriteText = vi.fn().mockResolvedValue(undefined);

beforeEach(() => {
  permState.perms = new Set(['payables:read', 'payables:write', 'payables:pay', 'treasury:read', 'treasury:write']);
  clipboardWriteText.mockClear();
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: clipboardWriteText }, configurable: true });
  mockUsePayable.mockReturnValue({ data: undefined });
  mockUseSummary.mockReturnValue({ data: summary });
  mockUsePayables.mockReturnValue({
    data: { items: [overdueItem, baseItem, manualItem, agreementItem], total: 4, totalPages: 1 },
  });
});

describe('PaymentQueueTab', () => {
  it('queries Scheduled unpaid installments and renders queue rows', () => {
    renderTab();
    expect(mockUsePayables).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'Scheduled', paid: false }),
      expect.anything(),
    );
    expect(screen.getByText('ACME')).toBeInTheDocument();
    expect(screen.getAllByText('92.802.784/0001-90').length).toBeGreaterThan(0);
    expect(screen.getAllByText('#31854541').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Ulbra S.A').length).toBeGreaterThan(0);
  });

  it('group headers use SUMMARY totals, not page sums, and Vencidos starts collapsed', () => {
    renderTab();
    // Overdue header from summary (73 across all pages; page only has 1 overdue row).
    expect(screen.getByText('Vencidos')).toBeInTheDocument();
    expect(screen.getByText(/73 parcela\(s\) · R\$ 311\.969,73/)).toBeInTheDocument();
    // Collapsed by default → the overdue row is hidden until the header is clicked.
    expect(screen.queryByText('Atrasada Ltda')).not.toBeInTheDocument();
    fireEvent.click(screen.getByText('Vencidos'));
    expect(screen.getByText('Atrasada Ltda')).toBeInTheDocument();
    // Day group header also uses the summary bucket (9 parcelas — page shows 3 rows that day).
    expect(screen.getByText(/9 parcela\(s\) · R\$ 1\.234,56/)).toBeInTheDocument();
  });

  it('net amounts render with cents (brlCents)', () => {
    renderTab();
    expect(screen.getAllByText('R$ 750,00').length).toBeGreaterThan(0);
  });

  it('renders a checkbox ONLY on remittance-selectable rows; selection drives the bulk bar and the dialog', () => {
    renderTab();
    const checkboxes = screen.getAllByRole('checkbox');
    // 3 rows visible (overdue collapsed): base + agreement are selectable → 2 checkboxes; the manual
    // rail row renders NO checkbox at all (not a disabled one).
    expect(checkboxes).toHaveLength(2);
    expect(checkboxes.some((c) => (c as HTMLInputElement).disabled)).toBe(false);
    fireEvent.click(checkboxes[0]);
    expect(screen.getByText(/1 selecionado\(s\) · R\$ 750,00/)).toBeInTheDocument();
    fireEvent.click(screen.getByText('Gerar remessa'));
    expect(screen.getByText(/1 parcela\(s\) selecionada\(s\)/)).toBeInTheDocument();
  });

  it('non-selectable manual row shows an "i" with the reason in the checkbox slot (method-aware)', () => {
    renderTab();
    // Manual Vendor: rail=Manual, método PIX, sem dados → no checkbox, tooltip names the missing PIX key.
    expect(screen.getByTitle('Fornecedor sem chave PIX cadastrada')).toBeInTheDocument();
  });

  it('boleto digitable line is click-to-copy with a "clique para copiar" hover + feedback', async () => {
    renderTab();
    const copyBtn = screen.getByTitle('Clique para copiar linha digitável');
    fireEvent.click(copyBtn);
    expect(clipboardWriteText).toHaveBeenCalledWith('8' + '4'.repeat(47));
    expect(await screen.findByText('Copiado!')).toBeInTheDocument();
  });

  it('shows the eligibility hint when no row in the list can be remitted', () => {
    mockUsePayables.mockReturnValue({ data: { items: [manualItem], total: 1, totalPages: 1 } });
    renderTab();
    expect(screen.getByText(/Nenhuma parcela elegível para remessa/)).toBeInTheDocument();
  });

  it('does not show the eligibility hint when at least one row is selectable', () => {
    renderTab();
    expect(screen.queryByText(/Nenhuma parcela elegível para remessa/)).not.toBeInTheDocument();
  });

  it('shows Dar baixa only for the manual rail (payables:pay) and chips for convênio/NF pendente', () => {
    renderTab();
    expect(screen.getAllByText('Dar baixa').length).toBe(1);
    expect(screen.getByText('CONVÊNIO')).toBeInTheDocument();
    expect(screen.getByText('NF pendente')).toBeInTheDocument();
  });

  it('without treasury:write there are no checkboxes and no bulk actions', () => {
    permState.perms = new Set(['payables:read', 'payables:pay']);
    renderTab();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
    expect(screen.queryByText('Gerar remessa')).not.toBeInTheDocument();
  });
});
