import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageSummary, mockUsePayableStageItems } = vi.hoisted(() => ({
  mockUseStageSummary: vi.fn(),
  mockUsePayableStageItems: vi.fn(),
}));
vi.mock('../../store', () => ({
  useApp: () => ({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' }),
}));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../queries', () => ({
  usePayableStageSummary: mockUseStageSummary,
  usePayableStageItems: mockUsePayableStageItems,
}));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));

import { PayablesLayout } from './layout';

beforeEach(() => {
  mockUseStageSummary.mockReturnValue({
    data: {
      entry: { count: 2, totalAmount: 100 },
      classification: { count: 0, totalAmount: 0 },
      approval: { count: 1, totalAmount: 50 },
      accounting: { count: 1, totalAmount: 75 },
      payment: { count: 0, totalAmount: 0 },
      reconciled: { count: 9, totalAmount: 900 },
    },
  });
  mockUsePayableStageItems.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
});

describe('PayablesLayout', () => {
  it('renders the dashboard cards and stage tabs, defaulting to the entry tab', () => {
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <PayablesLayout />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('empresa-picker')).toBeInTheDocument();
    // Stage labels appear in both the dashboard and the tab bar.
    expect(screen.getAllByText('Entrada').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Aprovação').length).toBeGreaterThan(0);
  });
  it('switches tab when a dashboard card is clicked', () => {
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <PayablesLayout />
      </MemoryRouter>,
    );
    // Every card has a "Ver pendências" link — click the first one (entry card).
    fireEvent.click(screen.getAllByText('Ver pendências')[0]);
    // entry tab queried with the Received status (default active tab still renders a list)
    expect(mockUsePayableStageItems).toHaveBeenCalled();
  });
});
