import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import '../../i18n';

vi.mock('../../store', () => ({
  useApp: () => ({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' }),
}));
vi.mock('../../queries', () => ({
  useUnits: () => ({ data: [{ id: 'unit_1', name: 'Canoas', acronym: 'CAN' }] }),
  useCostCenters: () => ({ data: [{ id: 'cc_1', code: '3.01.01', name: 'Material de consumo', unitIds: ['unit_1'] }] }),
  useSaveBudgetAdjustment: () => ({ mutate: vi.fn(), isPending: false, error: null }),
  useBudgetAdjustment: () => ({ data: undefined }),
}));
// The context panel fetches real budget data — irrelevant for the prefill.
vi.mock('./adjustment-context', () => ({ AdjustmentBudgetContext: () => null }));

import { NewBudgetAdjustmentPage } from './form';

function renderNew(search: string) {
  return render(
    <MemoryRouter initialEntries={[`/app/budget-adjustments/new${search}`]}>
      <Routes>
        <Route path='/app/budget-adjustments/new' element={<NewBudgetAdjustmentPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('Adequação — pré-preenchimento pela triagem', () => {
  it('semeia CC, unidade, competência e valor a partir dos query params', () => {
    renderNew('?costCenterId=cc_1&unitId=unit_1&competency=2026-07&value=800');
    expect(screen.getByDisplayValue('3.01.01 · Material de consumo')).toBeInTheDocument();
    expect(screen.getByDisplayValue('CAN — Canoas')).toBeInTheDocument();
    expect(screen.getByDisplayValue('2026-07')).toBeInTheDocument();
    expect(screen.getByDisplayValue('800')).toBeInTheDocument();
  });

  it('sem query params não vaza prefill', () => {
    renderNew('');
    expect(screen.queryByDisplayValue('800')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('2026-07')).not.toBeInTheDocument();
  });
});
