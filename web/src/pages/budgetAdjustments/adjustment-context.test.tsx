import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';

const useDimensionBudgetControl = vi.fn();
vi.mock('../../queries', () => ({
  useDimensionBudgetControl: (...args: unknown[]) => useDimensionBudgetControl(...args),
}));

import { AdjustmentBudgetContext } from './adjustment-context';

function withMonth(orcado: number, available: number, budgeted = true) {
  useDimensionBudgetControl.mockReturnValue({ data: { months: [{ orcado, available, budgeted }] } });
}

describe('AdjustmentBudgetContext — antes → ajuste → depois', () => {
  beforeEach(() => useDimensionBudgetControl.mockReset());

  it('Suplementação soma ao teto e ao disponível', () => {
    withMonth(112_000, 100_000);
    render(<AdjustmentBudgetContext unitId='u' costCenterId='c' competency='2026-08' value={40_000} direction='add' />);
    expect(screen.getByText(/R\$\s*112\.000,00/)).toBeInTheDocument(); // current ceiling
    expect(screen.getByText(/\+\s*R\$\s*40\.000,00/)).toBeInTheDocument(); // adjustment (+)
    expect(screen.getByText(/R\$\s*152\.000,00/)).toBeInTheDocument(); // resulting ceiling = 112k + 40k
    expect(screen.getByText(/R\$\s*140\.000,00/)).toBeInTheDocument(); // resulting available = 100k + 40k
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('Redução subtrai do teto e alerta quando passa do disponível', () => {
    withMonth(112_000, 30_000);
    render(
      <AdjustmentBudgetContext unitId='u' costCenterId='c' competency='2026-08' value={40_000} direction='remove' />,
    );
    expect(screen.getByText(/R\$\s*72\.000,00/)).toBeInTheDocument(); // resulting ceiling = 112k − 40k
    expect(screen.getByRole('alert')).toBeInTheDocument(); // resulting available = −10k → overflow
  });

  it('sem competência mostra a dica em vez de números', () => {
    render(<AdjustmentBudgetContext unitId='u' costCenterId='c' competency='' value={40_000} direction='add' />);
    expect(screen.getByText('Selecione a competência para ver o contexto.')).toBeInTheDocument();
  });
});
