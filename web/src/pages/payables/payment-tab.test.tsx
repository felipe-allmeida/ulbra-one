import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUsePayables } = vi.hoisted(() => ({ mockUsePayables: vi.fn() }));
vi.mock('../../queries', () => ({ usePayables: mockUsePayables, usePayable: vi.fn() }));

import { PaymentTab } from './payment-tab';

const item = {
  payableId: 'payable_1',
  installmentId: 'i1',
  sequence: 1,
  installmentCount: 1,
  counterpartyName: 'ACME',
  description: 'NF 1',
  costCenterId: 'cc_1',
  categoryId: null,
  dueDate: '2026-06-20',
  amount: 800,
  installmentStatus: 'Open',
  payableStatus: 'Scheduled',
};

beforeEach(() => mockUsePayables.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } }));

describe('PaymentTab', () => {
  it('queries Scheduled payables and renders a period group with the vendor + amount', () => {
    render(
      <MemoryRouter>
        <PaymentTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(mockUsePayables).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'Scheduled' }),
      expect.anything(),
    );
    expect(screen.getByText('ACME')).toBeInTheDocument();
    expect(screen.getAllByText(/R\$/).length).toBeGreaterThan(0);
    expect(screen.getByText(/Total:/)).toBeInTheDocument();
  });
});
