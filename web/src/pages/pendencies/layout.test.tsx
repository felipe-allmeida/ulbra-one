import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseApp, mockUseStageSummary } = vi.hoisted(() => ({
  mockUseApp: vi.fn(),
  mockUseStageSummary: vi.fn(),
}));
vi.mock('../../store', () => ({ useApp: mockUseApp }));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../queries', () => ({ usePayableStageSummary: mockUseStageSummary }));
vi.mock('../payables', () => ({ ApprovalTab: () => <div data-testid='approval-tab' /> }));
vi.mock('./fiscal-tab', () => ({ FiscalTab: () => <div data-testid='fiscal-tab' /> }));

import { PendenciesPage } from './layout';

beforeEach(() => {
  mockUseApp.mockReturnValue({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' });
  mockUseStageSummary.mockReturnValue({
    data: {
      entry: { count: 0, totalAmount: 0 },
      classification: { count: 0, totalAmount: 0 },
      approval: { count: 3, totalAmount: 300 },
      accounting: { count: 5, totalAmount: 500 },
      payment: { count: 0, totalAmount: 0 },
      reconciled: { count: 0, totalAmount: 0 },
      fiscalDocumentPending: { count: 2, totalAmount: 200 },
    },
  });
});

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <PendenciesPage />
    </MemoryRouter>,
  );
}

describe('PendenciesPage', () => {
  it('defaults to the Aprovação tab and shows both tab labels', () => {
    renderAt('/app/pendencies');
    expect(screen.getByTestId('approval-tab')).toBeInTheDocument();
    expect(screen.queryByTestId('fiscal-tab')).not.toBeInTheDocument();
    expect(screen.getByText('Aprovação')).toBeInTheDocument();
    expect(screen.getByText('Nota Fiscal')).toBeInTheDocument();
  });

  it('activates the Nota Fiscal tab from the ?tab=fiscal query param', () => {
    renderAt('/app/pendencies?tab=fiscal');
    expect(screen.getByTestId('fiscal-tab')).toBeInTheDocument();
    expect(screen.queryByTestId('approval-tab')).not.toBeInTheDocument();
  });

  it('renders the pendency counts from the stage summary (approval + fiscalDocumentPending)', () => {
    renderAt('/app/pendencies');
    expect(screen.getByText('3')).toBeInTheDocument(); // approval.count
    expect(screen.getByText('2')).toBeInTheDocument(); // fiscalDocumentPending.count
  });

  it('shows an empty state when no company is selected', () => {
    mockUseApp.mockReturnValue({ empresa: null, empresaId: '' });
    renderAt('/app/pendencies');
    expect(screen.queryByTestId('approval-tab')).not.toBeInTheDocument();
    expect(screen.getByText('Nenhuma empresa')).toBeInTheDocument();
  });
});
