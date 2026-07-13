import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageItems } = vi.hoisted(() => ({ mockUseStageItems: vi.fn() }));
vi.mock('../../queries', () => ({ usePayableStageItems: mockUseStageItems }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));
vi.mock('./accounting-drawer', () => ({
  AccountingDrawer: ({ payableId }: { payableId: string }) => (
    <div data-testid='accounting-drawer'>{payableId}</div>
  ),
}));

import { AccountingTab } from './accounting-tab';

beforeEach(() => {
  mockUseStageItems.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
});

function renderTab() {
  return render(
    <MemoryRouter>
      <AccountingTab companyId='company_1' />
    </MemoryRouter>,
  );
}

describe('AccountingTab', () => {
  it('queries PendingAccounting payables excluding advances (tracked in their own tab)', () => {
    renderTab();
    expect(mockUseStageItems).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'PendingAccounting', excludeOrigin: 'Advance' }),
      expect.anything(),
    );
  });

  it('shows the empty state with no items', () => {
    renderTab();
    expect(screen.getByText(/nenhum/i)).toBeInTheDocument();
  });
});
