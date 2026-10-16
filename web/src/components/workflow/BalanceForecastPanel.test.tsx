import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import '../../i18n';

const { mockUseCashFlow } = vi.hoisted(() => ({ mockUseCashFlow: vi.fn() }));
vi.mock('../../queries', () => ({ useCashFlow: mockUseCashFlow }));

import { BalanceForecastPanel } from './BalanceForecastPanel';

const cashFlow = {
  startingBalance: 20000,
  kpis: { lowestProjectedBalance: 5000, runwayPeriods: 4, netResult: 1200 },
  periods: [
    { label: 'Jun', closingBalance: 12450, inflowForecast: 8000, outflowForecast: 6000, belowReserve: false },
    { label: 'Jul', closingBalance: 9800, inflowForecast: 7000, outflowForecast: 9650, belowReserve: false },
  ],
};

beforeEach(() => mockUseCashFlow.mockReset());

describe('BalanceForecastPanel', () => {
  it('renders KPIs and the impact label when open', () => {
    mockUseCashFlow.mockReturnValue({ data: cashFlow, isLoading: false });
    render(
      <BalanceForecastPanel companyId='company_1' impactAmount={2450} open onClose={() => {}} onConfirm={() => {}} />,
    );
    expect(screen.getByText('Menor saldo projetado')).toBeInTheDocument();
    expect(screen.getByText('Impacto da aprovação')).toBeInTheDocument();
    expect(screen.getByText('R$ 2.450,00')).toBeInTheDocument();
  });
  it('calls onConfirm when the confirm button is clicked', () => {
    mockUseCashFlow.mockReturnValue({ data: cashFlow, isLoading: false });
    const onConfirm = vi.fn();
    render(
      <BalanceForecastPanel companyId='company_1' impactAmount={2450} open onClose={() => {}} onConfirm={onConfirm} />,
    );
    fireEvent.click(screen.getByText('Confirmar aprovação'));
    expect(onConfirm).toHaveBeenCalledOnce();
  });
});
