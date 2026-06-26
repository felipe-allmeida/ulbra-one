import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseReceivables } = vi.hoisted(() => ({ mockUseReceivables: vi.fn() }));
vi.mock('../../queries', () => ({ useReceivables: mockUseReceivables, useReceivable: vi.fn() }));

import { AReceberTab } from './areceber-tab';

const item = {
  receivableId: 'rcv_1',
  installmentId: 'i1',
  sequence: 1,
  installmentCount: 1,
  payerName: 'Cliente A',
  description: 'Mensalidade',
  costCenterId: 'cc_1',
  amount: 500,
  dueDate: '2026-06-20',
  receivableStatus: 'AwaitingReceipt',
  installmentStatus: 'Open',
  isOverdue: true,
};

beforeEach(() => mockUseReceivables.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } }));

describe('AReceberTab', () => {
  it('queries AwaitingReceipt and renders the payer + a value summary', () => {
    render(
      <MemoryRouter>
        <AReceberTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(mockUseReceivables).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'AwaitingReceipt' }),
      expect.anything(),
    );
    expect(screen.getByText('Cliente A')).toBeInTheDocument();
    expect(screen.getByText(/Total:/)).toBeInTheDocument();
    expect(screen.getAllByText(/R\$/).length).toBeGreaterThan(0);
  });
});
