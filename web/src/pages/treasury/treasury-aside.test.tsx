import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';

const { mockSummary, mockPeriod } = vi.hoisted(() => ({ mockSummary: vi.fn(), mockPeriod: vi.fn() }));
vi.mock('../../queries', () => ({
  usePaymentQueueSummary: mockSummary,
  usePeriodSummary: mockPeriod,
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
}));

import { TreasuryAside } from './treasury-aside';

const iso = (offsetDays: number) => {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  return d.toISOString().slice(0, 10);
};

beforeEach(() => {
  mockSummary.mockReturnValue({
    data: {
      asOf: iso(0),
      overdueCount: 3,
      overdueNetTotal: 1500,
      groups: [
        { dueDate: iso(0), count: 2, netTotal: 500 },
        { dueDate: iso(3), count: 1, netTotal: 200 }, // next 7
        { dueDate: iso(10), count: 1, netTotal: 300 }, // 8–14
        { dueDate: iso(30), count: 1, netTotal: 9999 }, // outside the window
      ],
      totalCount: 8,
      totalNetTotal: 12499,
    },
  });
  mockPeriod.mockReturnValue({
    data: {
      reconciled: { count: 1, total: 0 },
      suggested: { count: 2, total: 0 },
      unmatched: { count: 1, total: 0 },
      ignored: { count: 0, total: 0 },
    },
  });
});

describe('TreasuryAside', () => {
  it('shows the 14-day outflow derived only from scheduled installments (no balance claims)', () => {
    render(<TreasuryAside companyId='c1' />);
    // 1500 (overdue) + 500 (today) + 200 (next7) + 300 (8–14) = 2500 — the 30-day bucket stays out.
    expect(screen.getByText('R$ 2.500,00')).toBeInTheDocument();
    expect(screen.getByText('R$ 1.500,00')).toBeInTheDocument();
    expect(screen.getByText('R$ 500,00')).toBeInTheDocument();
    expect(screen.getByText('R$ 200,00')).toBeInTheDocument();
    expect(screen.getByText('R$ 300,00')).toBeInTheDocument();
    expect(screen.queryByText(/saldo/i)).not.toBeInTheDocument();
  });

  it('shows the KPI tiles: due today, overdue, queue total and to-reconcile', () => {
    render(<TreasuryAside companyId='c1' />);
    expect(screen.getByText('Vencem hoje')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
    // "Vencidos" appears twice: cash-outflow row + KPI tile.
    expect(screen.getAllByText('Vencidos').length).toBe(2);
    expect(screen.getByText('R$ 12.499,00')).toBeInTheDocument(); // A pagar (fila)
    expect(screen.getByText('A conciliar')).toBeInTheDocument();
    expect(screen.getAllByText('3').length).toBeGreaterThan(0); // vencidos count e A conciliar = 3
  });

  it('warns about overdue installments', () => {
    render(<TreasuryAside companyId='c1' />);
    expect(screen.getByText(/3 parcela\(s\) vencida\(s\) somando R\$ 1\.500,00/)).toBeInTheDocument();
  });
});
