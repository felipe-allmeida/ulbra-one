import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

const { mockUsePayable, mockClassify } = vi.hoisted(() => ({ mockUsePayable: vi.fn(), mockClassify: vi.fn() }));
vi.mock('../../queries', () => ({
  usePayable: mockUsePayable,
  useClassifyPayable: () => ({ mutate: mockClassify, isPending: false }),
  useLedgerAccounts: () => ({
    data: [{ id: 'ledgacct_1', code: '3.1', name: 'Despesas' }],
  }),
}));

import { ClassificationDrawer } from './classification-drawer';

beforeEach(() => {
  mockUsePayable.mockReturnValue({
    data: {
      id: 'payable_1',
      companyId: 'company_1',
      counterpartyName: 'ACME',
      suggestedAccountingAccountId: 'ledgacct_1',
      classificationRationale: 'Fornecedor recorrente de TI',
      accountingAccountId: null,
    },
  });
});

describe('ClassificationDrawer', () => {
  it('pre-selects the AI-suggested accounts and shows the rationale', () => {
    renderWithProviders(<ClassificationDrawer payableId='payable_1' onClose={() => {}} />);
    // The suggested accounting account label is visible in the trigger (Combobox renders the selected item's label).
    expect(screen.getByText('Despesas')).toBeInTheDocument();
    expect(screen.getByText(/Fornecedor recorrente/)).toBeInTheDocument();
  });
});
