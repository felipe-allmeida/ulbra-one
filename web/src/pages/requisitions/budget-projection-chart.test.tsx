import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { BudgetProjectionChart, type ProjectionPoint } from './budget-projection-chart';

const legend = { ceiling: 'Teto', consumption: 'Consumo', over: 'Acima do teto' };

function pt(competency: string, ceiling: number, consumed: number, budgeted = true): ProjectionPoint {
  return { competency, ceiling, consumed, budgeted };
}

describe('BudgetProjectionChart', () => {
  it('marca ponto vermelho onde o consumo passa do teto (estouro)', () => {
    // 08 estoura (consumo projetado acima do teto).
    const points = [pt('2026-07', 5000, 2000), pt('2026-08', 1000, 2000), pt('2026-09', 5000, 3000)];
    render(<BudgetProjectionChart points={points} breaches={['2026-08']} ariaLabel='proj' legend={legend} />);
    expect(screen.getAllByTestId('bpj-breach')).toHaveLength(1);
    expect(screen.getByRole('img')).toBeInTheDocument();
  });

  it('sem estouro não marca ponto vermelho', () => {
    const points = [pt('2026-07', 5000, 2000), pt('2026-08', 5000, 3000)];
    render(<BudgetProjectionChart points={points} breaches={[]} ariaLabel='proj' legend={legend} />);
    expect(screen.queryAllByTestId('bpj-breach')).toHaveLength(0);
    expect(screen.getByRole('img')).toBeInTheDocument();
  });

  it('mês sem orçamento vira vértice vazado (teto quebra)', () => {
    const points = [pt('2026-07', 5000, 2000), pt('2026-08', 0, 0, false)];
    render(<BudgetProjectionChart points={points} breaches={[]} ariaLabel='proj' legend={legend} />);
    expect(screen.getAllByTestId('bpj-nobudget')).toHaveLength(1);
  });

  it('menos de 2 pontos não renderiza', () => {
    const { container } = render(
      <BudgetProjectionChart points={[pt('2026-07', 5000, 2000)]} breaches={[]} ariaLabel='proj' legend={legend} />,
    );
    expect(container.firstChild).toBeNull();
  });
});
