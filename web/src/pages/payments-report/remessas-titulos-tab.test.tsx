import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../test/renderWithProviders';
import { setViewportMatches } from '../../test/setup';
import '../../i18n';
import type { RemittanceLineItem } from '../../api';

// RemessasTitulosTab reads from useRemittances/useRemittanceLines/useUnits/useCostCenters — mock the
// whole module so the render is deterministic off fixed data (same pattern as relacao-tab.test.tsx).
const { mockUseRemittances, mockUseRemittanceLines, mockUseUnits, mockUseCostCenters } = vi.hoisted(() => ({
  mockUseRemittances: vi.fn(),
  mockUseRemittanceLines: vi.fn(),
  mockUseUnits: vi.fn(),
  mockUseCostCenters: vi.fn(),
}));

vi.mock('../../queries', () => ({
  useRemittances: mockUseRemittances,
  useRemittanceLines: mockUseRemittanceLines,
  useRemittance: vi.fn(() => ({ data: undefined, isLoading: false })),
  useUnits: mockUseUnits,
  useCostCenters: mockUseCostCenters,
}));

import { RemessasTitulosTab } from './remessas-titulos-tab';

// One título line exercising the folded field from Task 17b: requisitionCode (relocated — was
// folded into `nature`'s detail cell, phone-only).
const LINE: RemittanceLineItem = {
  id: 'line_1',
  remittanceId: 'remittance_1',
  remittanceSequence: 42,
  category: 'Pix',
  generatedAt: '2026-08-20T10:00:00Z',
  payableId: 'payable_1',
  installmentId: 'installment_1',
  requisitionCode: 1024,
  favoredName: 'Microsoft Informática Ltda.',
  unitId: 'unit_1',
  unitName: 'Canoas/RS',
  costCenterId: 'cc_1',
  natureName: 'Tecnologia e Inovação',
  method: 'Pix',
  amount: 8000,
  dueDate: '2026-09-12',
  status: 'Pending',
  effectiveDate: null,
  returnReason: null,
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUseUnits.mockReturnValue({ data: [], isLoading: false });
  mockUseCostCenters.mockReturnValue({ data: [], isLoading: false });
  mockUseRemittances.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 }, isLoading: false });
  mockUseRemittanceLines.mockReturnValue({
    data: { items: [LINE], total: 1, totalPages: 1 },
    isLoading: false,
  });
});

function renderTab() {
  renderWithProviders(
    <MemoryRouter>
      <RemessasTitulosTab companyId='c1' />
    </MemoryRouter>,
  );
  // TitulosListTable only mounts under the "Títulos gerados" sub-view — the default is "Remessas".
  fireEvent.click(screen.getByRole('button', { name: 'Títulos gerados' }));
}

describe('TitulosListTable desktop', () => {
  it('mantém as 8 colunas/headers originais (Task 17b não vaza no desktop)', () => {
    renderTab();
    const headers = [...document.querySelectorAll('table thead th')].map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Remessa', 'Fornecedor / CNPJ', 'Unidade', 'Natureza', 'Total', 'Venc', 'Status', '']);
  });
});

describe('TitulosListTable no telefone', () => {
  beforeEach(() => setViewportMatches(true));

  it('rotula o código da requisição em coluna própria, não emendado sob "Natureza" (Task 17b)', () => {
    renderTab();
    fireEvent.click(screen.getByRole('button', { name: /expandir/i }));

    const reqLabel = screen.getByText('Requisição', { selector: '.rt-k' });
    expect(reqLabel.nextElementSibling).toHaveTextContent('#1024');

    // O host original ("Natureza") só mostra o nome da natureza — não o código da requisição
    // dobrado dentro de si.
    const natureLabel = screen.getByText('Natureza', { selector: '.rt-k' });
    expect(natureLabel.nextElementSibling).toHaveTextContent('Tecnologia e Inovação');
    expect(natureLabel.nextElementSibling).not.toHaveTextContent('#1024');
  });
});
