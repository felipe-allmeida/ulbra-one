import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

// The shell test focuses on tab routing/counts/search — the tabs themselves have their own tests.
vi.mock('./payment-queue-tab', () => ({
  PaymentQueueTab: ({ search }: { search: string }) => <div>QUEUE_TAB {search}</div>,
}));
vi.mock('./history-tab', () => ({ PaymentsHistoryTab: () => <div>HISTORY_TAB</div> }));
vi.mock('./treasury-aside', () => ({ TreasuryAside: () => <div>ASIDE</div> }));
vi.mock('../remessas/list', () => ({
  RemittancesTab: ({ focusRemittanceId }: { focusRemittanceId?: string | null }) => (
    <div>REMITTANCES_TAB {focusRemittanceId ?? ''}</div>
  ),
}));

const { mockSummary, mockRemittances, mockHistory } = vi.hoisted(() => ({
  mockSummary: vi.fn(),
  mockRemittances: vi.fn(),
  mockHistory: vi.fn(),
}));
vi.mock('../../queries', () => ({
  usePaymentQueueSummary: mockSummary,
  useRemittances: mockRemittances,
  usePaymentsHistory: mockHistory,
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
}));
vi.mock('../../store', () => ({
  useApp: () => ({
    empresa: { id: 'c1', legalName: 'Empresa Teste' },
    empresaId: 'c1',
    empresas: [{ id: 'c1', legalName: 'Empresa Teste' }],
    isAllCompanies: false,
    scopeReady: true,
    setEmpresaId: vi.fn(),
  }),
}));

import { TreasuryHomePage } from './home';

const renderAt = (url: string) =>
  render(
    <MemoryRouter initialEntries={[url]}>
      <TreasuryHomePage />
    </MemoryRouter>,
  );

beforeEach(() => {
  mockSummary.mockReturnValue({
    data: {
      asOf: '2026-08-04',
      overdueCount: 73,
      overdueNetTotal: 311969.73,
      groups: [],
      totalCount: 117,
      totalNetTotal: 1,
    },
  });
  mockRemittances.mockReturnValue({ data: { items: [], total: 83, totalPages: 1 } });
  mockHistory.mockReturnValue({ data: { items: [], total: 215, totalPages: 1 } });
});

describe('TreasuryHomePage', () => {
  it('renders the 3 tabs with whole-set counts and lands on the queue (no Conciliação — separate screen)', () => {
    renderAt('/app/treasury');
    expect(screen.getByText('Gestão Financeira')).toBeInTheDocument();
    expect(screen.getByText('Fila de Pagamentos')).toBeInTheDocument();
    expect(screen.getByText('117')).toBeInTheDocument();
    expect(screen.getByText('Remessas')).toBeInTheDocument();
    expect(screen.getByText('83')).toBeInTheDocument();
    expect(screen.getByText('Histórico')).toBeInTheDocument();
    expect(screen.getByText('215')).toBeInTheDocument();
    // Conciliação is NOT a tab here — it lives at /app/reconciliation.
    expect(screen.queryByText('Conciliação')).not.toBeInTheDocument();
    expect(screen.getByText(/QUEUE_TAB/)).toBeInTheDocument();
    expect(screen.getByText('ASIDE')).toBeInTheDocument();
  });

  it('?tab= selects the tab (history)', () => {
    renderAt('/app/treasury?tab=history');
    expect(screen.getByText('HISTORY_TAB')).toBeInTheDocument();
    expect(screen.queryByText(/QUEUE_TAB/)).not.toBeInTheDocument();
  });

  it('?remittance= deep link forces the remittances tab and forwards the id', () => {
    renderAt('/app/treasury?remittance=rem_123');
    expect(screen.getByText(/REMITTANCES_TAB rem_123/)).toBeInTheDocument();
  });
});
