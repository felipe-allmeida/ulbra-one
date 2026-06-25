import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';

const { mockUseCashFlow } = vi.hoisted(() => ({ mockUseCashFlow: vi.fn() }));
vi.mock('../../queries', () => ({ useCashFlow: mockUseCashFlow }));

import { BalanceForecastBanner } from './BalanceForecastBanner';

const cashFlow = {
  startingBalance: 20000,
  kpis: { lowestProjectedBalance: 5000, runwayPeriods: 4, netResult: 1200 },
  periods: [{ closingBalance: 12450, belowReserve: false }],
};

beforeEach(() => mockUseCashFlow.mockReset());

describe('BalanceForecastBanner', () => {
  it('shows the projected closing balance', () => {
    mockUseCashFlow.mockReturnValue({ data: cashFlow, isLoading: false });
    render(<BalanceForecastBanner companyId='company_1' />);
    expect(screen.getByText('Saldo projetado')).toBeInTheDocument();
    expect(screen.getByText('R$ 12.450')).toBeInTheDocument();
  });
  it('shows the after-approval balance when additionalOutflow is provided', () => {
    mockUseCashFlow.mockReturnValue({ data: cashFlow, isLoading: false });
    render(<BalanceForecastBanner companyId='company_1' additionalOutflow={2450} />);
    expect(screen.getByText('Saldo após aprovação')).toBeInTheDocument();
    expect(screen.getByText('R$ 10.000')).toBeInTheDocument();
  });
  it('renders nothing meaningful while loading', () => {
    mockUseCashFlow.mockReturnValue({ data: undefined, isLoading: true });
    const { container } = render(<BalanceForecastBanner companyId='company_1' />);
    expect(container.querySelector('.forecast-banner-loading')).toBeTruthy();
  });
});
