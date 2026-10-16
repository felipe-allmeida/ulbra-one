import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';
import { brl } from '../../lib';

// The "Em aprovação" tile sums the value server-side (totals.Approval from stage-counts) instead of the old
// client sum over the first page of the Approval list — this test guards that wiring.
const APPROVAL_TOTAL = 187654.32;

vi.mock('../../queries', () => ({
  useCostCenterBudgetSummary: () => ({ data: [], isLoading: false }),
  useRequisitionStageCounts: () => ({
    counts: {
      Complement: 1,
      FinancialComplement: 0,
      Draft: 2,
      Triage: 3,
      Approval: 4,
      InPayment: 0,
      Paid: 5,
      Finalized: 6,
    },
    totals: { Approval: APPROVAL_TOTAL },
    isLoading: false,
  }),
}));

import { RequesterAside } from './requester-aside';

describe('RequesterAside', () => {
  it('mostra o total "Em aprovação" a partir de totals.Approval (soma do servidor), formatado em BRL', () => {
    render(<RequesterAside empresaId='company_1' />);
    // brl() uses a non-breaking space; testing-library normalizes it to a regular space, so normalize the
    // expected too (\s matches the nbsp). The currency string is unique among the tiles (others are integers).
    const expected = brl(APPROVAL_TOTAL).replace(/\s/g, ' ');
    expect(screen.getByText(expected)).toBeInTheDocument();
  });
});
