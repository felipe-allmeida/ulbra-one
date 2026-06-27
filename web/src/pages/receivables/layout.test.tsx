import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageSummary, mockUseReceivables, mockUseReceivableStageItems } = vi.hoisted(() => ({
  mockUseStageSummary: vi.fn(),
  mockUseReceivables: vi.fn(),
  mockUseReceivableStageItems: vi.fn(),
}));
vi.mock('../../store', () => ({
  useApp: () => ({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' }),
}));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../queries', () => ({
  useReceivableStageSummary: mockUseStageSummary,
  useReceivables: mockUseReceivables,
  useReceivableStageItems: mockUseReceivableStageItems,
  useReceivable: vi.fn(),
}));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {} }) }));

import { ReceivablesLayout } from './layout';

beforeEach(() => {
  mockUseStageSummary.mockReturnValue({
    data: {
      entry: { count: 2, totalAmount: 100 },
      receipt: { count: 1, totalAmount: 50 },
      reconciled: { count: 9, totalAmount: 900 },
    },
  });
  mockUseReceivables.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
  mockUseReceivableStageItems.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
});

describe('ReceivablesLayout', () => {
  it('renders dashboard cards + two tabs, defaulting to A receber', () => {
    render(
      <MemoryRouter initialEntries={['/app/receivables']}>
        <ReceivablesLayout />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('empresa-picker')).toBeInTheDocument();
    expect(screen.getAllByText('A receber').length).toBeGreaterThan(0);
    expect(screen.getByText('Recebimento')).toBeInTheDocument(); // dashboard card
  });
  it('clicking the Conciliado card switches to the reconciled tab', () => {
    render(
      <MemoryRouter initialEntries={['/app/receivables']}>
        <ReceivablesLayout />
      </MemoryRouter>,
    );
    const links = screen.getAllByText('Ver pendências');
    fireEvent.click(links[links.length - 1]); // reconciled card
    expect(mockUseReceivableStageItems).toHaveBeenCalled();
  });
});
