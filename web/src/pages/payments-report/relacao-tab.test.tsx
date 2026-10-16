import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../test/renderWithProviders';
import { setViewportMatches } from '../../test/setup';
import '../../i18n';
import type { PaymentReportItem } from '../../api';

// RelacaoTab reads from usePaymentsReport/useUnits/useCostCenters — mock the whole module so the
// render is deterministic off fixed data, same pattern as cash-flow.test.tsx / users.test.tsx.
const { mockUsePaymentsReport, mockUseUnits, mockUseCostCenters } = vi.hoisted(() => ({
  mockUsePaymentsReport: vi.fn(),
  mockUseUnits: vi.fn(),
  mockUseCostCenters: vi.fn(),
}));

vi.mock('../../queries', () => ({
  usePaymentsReport: mockUsePaymentsReport,
  useUnits: mockUseUnits,
  useCostCenters: mockUseCostCenters,
}));

import { RelacaoTab } from './relacao-tab';

// One row exercising every folded field from Task 17b: requisitionCode/requesterName (host,
// "Cód. / Solicitante"), vendorDocument (relocated, was folded here) and dueDate (relocated, was
// folded here too).
const ITEM: PaymentReportItem = {
  payableId: 'payable_1',
  installmentId: 'installment_1',
  companyId: 'c1',
  requisitionCode: 1024,
  requesterName: 'Nayara da Rocha Silveira',
  unitId: 'unit_1',
  unitName: 'Canoas/RS',
  costCenterId: 'cc_1',
  natureName: 'Tecnologia e Inovação',
  approverName: 'Não identificado',
  approvedAt: '2026-08-28T15:18:00Z',
  counterpartyName: 'Microsoft Informática Ltda.',
  vendorDocument: '60316817000103',
  amount: 8000,
  dueDate: '2027-01-12',
  installmentSequence: 5,
  installmentCount: 12,
  payableStatus: 'Scheduled',
  installmentStatus: 'Open',
  statusLabel: 'Fila Pagamento',
  paymentTypeLabel: 'Pgto 5/12',
  description: null,
  observation: null,
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUseUnits.mockReturnValue({ data: [], isLoading: false });
  mockUseCostCenters.mockReturnValue({ data: [], isLoading: false });
  mockUsePaymentsReport.mockReturnValue({
    data: { items: [ITEM], total: 1, totalPages: 1 },
    isLoading: false,
  });
});

function renderTab() {
  return renderWithProviders(
    <MemoryRouter>
      <RelacaoTab companyId='c1' scopeReady />
    </MemoryRouter>,
  );
}

describe('RelacaoTable desktop', () => {
  it('mantém as 7 colunas/headers originais (Task 17b não vaza no desktop)', () => {
    renderTab();
    const headers = [...document.querySelectorAll('table thead th')].map((th) => th.textContent?.trim());
    expect(headers).toEqual([
      'Cód. / Solicitante',
      'Unidade / Natureza',
      'Aprovador / Data',
      'Fornecedor / CNPJ',
      'Valor / Vencimento',
      'Status',
      '',
    ]);
  });
});

describe('RelacaoTable no telefone', () => {
  beforeEach(() => setViewportMatches(true));

  it('rotula documento e vencimento em colunas próprias, não emendados sob "Cód. / Solicitante" (Task 17b)', () => {
    renderTab();
    fireEvent.click(screen.getByRole('button', { name: /expandir/i }));

    // Cada valor relocado tem seu próprio `.rt-k` — não o header da coluna vizinha onde já viveu.
    const docLabel = screen.getByText('Documento', { selector: '.rt-k' });
    expect(docLabel.nextElementSibling).toHaveTextContent('60.316.817/0001-03');

    const dueLabel = screen.getByText('Vencimento', { selector: '.rt-k' });
    expect(dueLabel.nextElementSibling).toHaveTextContent('12/01/2027');

    // O host original ("Cód. / Solicitante") continua só com código+solicitante — não carrega mais
    // o CNPJ nem o vencimento dobrados dentro de si.
    const codeLabel = screen.getByText('Cód. / Solicitante', { selector: '.rt-k' });
    expect(codeLabel.nextElementSibling).toHaveTextContent('#1024');
    expect(codeLabel.nextElementSibling).toHaveTextContent('Nayara da Rocha Silveira');
    expect(codeLabel.nextElementSibling).not.toHaveTextContent('60.316.817/0001-03');
    expect(codeLabel.nextElementSibling).not.toHaveTextContent('12/01/2027');
  });
});
