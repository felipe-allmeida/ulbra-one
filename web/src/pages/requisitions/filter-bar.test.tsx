import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import '../../i18n';

vi.mock('../../queries', () => ({
  useUnits: () => ({ data: [{ id: 'unit_1', name: 'Campus Teste', acronym: 'CT' }] }),
  useCostCenters: () => ({ data: [{ id: 'cc_1', name: 'Marketing', code: 'MKT' }] }),
}));

import { RequisitionFilterBar, EMPTY_FILTERS, type RequisitionFilters } from './filter-bar';

const base: RequisitionFilters = { ...EMPTY_FILTERS };

describe('RequisitionFilterBar', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('propaga a busca somente após o debounce', () => {
    const onChange = vi.fn();
    render(<RequisitionFilterBar companyId='company_1' value={base} onChange={onChange} />);
    const input = screen.getByPlaceholderText('Buscar por código, fornecedor ou descrição…');
    fireEvent.change(input, { target: { value: 'cadeira' } });
    expect(onChange).not.toHaveBeenCalled(); // ainda dentro da janela de debounce
    vi.advanceTimersByTime(260);
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ search: 'cadeira' }));
  });

  it('alterna um chip de status', () => {
    const onChange = vi.fn();
    render(
      <RequisitionFilterBar
        companyId='company_1'
        value={base}
        onChange={onChange}
        statusOptions={['Paid', 'Cancelled']}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Paga' }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ statuses: ['Paid'] }));
  });

  it('limpa todos os filtros', () => {
    const onChange = vi.fn();
    const dirty: RequisitionFilters = { ...EMPTY_FILTERS, search: 'x', unitId: 'unit_1' };
    render(<RequisitionFilterBar companyId='company_1' value={dirty} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: 'Limpar' }));
    expect(onChange).toHaveBeenCalledWith(EMPTY_FILTERS);
  });
});

describe('RequisitionFilterBar — chip de status selecionado', () => {
  it('marca o chip selecionado com "on" mantendo a base rqf-chip (regressão: era rqf-chipon = sem estilo)', () => {
    render(
      <RequisitionFilterBar
        companyId='company_1'
        value={{ ...EMPTY_FILTERS, statuses: ['Paid'] }}
        onChange={() => {}}
        statusOptions={['Cancelled', 'Paid']}
      />,
    );

    const paid = screen.getByRole('button', { name: 'Paga' });
    const cancelled = screen.getByRole('button', { name: 'Cancelada' });

    // Selected: base class kept AND the `.rqf-chip.on` modifier applied (needs a space in the template).
    expect(paid).toHaveClass('rqf-chip', 'on');
    expect(paid).toHaveAttribute('aria-pressed', 'true');
    // Unselected: only the base class, never the malformed single token.
    expect(cancelled).toHaveClass('rqf-chip');
    expect(cancelled).not.toHaveClass('on');
    expect(cancelled).not.toHaveClass('rqf-chipon');
  });
});
