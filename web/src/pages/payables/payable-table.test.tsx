import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const navigate = vi.fn();
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigate };
});
vi.mock('./use-names', () => ({
  useNames: () => ({ centros: [], centroNome: { cc_1: 'Marketing' }, categoriaNome: {} }),
}));

import { PayableTable } from './payable-table';

const items = [
  {
    payableId: 'payable_1',
    counterpartyName: 'ACME',
    description: 'NF 100',
    costCenterId: 'cc_1',
    categoryId: null,
    firstDueDate: '2026-06-30',
    installmentCount: 3,
    totalAmount: 3702,
    status: 'Received',
  },
] as any;

describe('PayableTable', () => {
  it('renders rows and navigates to detail on row click', () => {
    render(
      <MemoryRouter>
        <PayableTable
          companyId='company_1'
          stageLabel='Entrada'
          items={items}
          total={1}
          page={1}
          totalPages={1}
          onPage={() => {}}
        />
      </MemoryRouter>,
    );
    expect(screen.getByText('NF 100')).toBeInTheDocument();
    expect(screen.getByText('ACME')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
    fireEvent.click(screen.getByText('NF 100'));
    expect(navigate).toHaveBeenCalledWith('/app/payables/payable_1');
  });
  it('shows an empty state when there are no items', () => {
    render(
      <MemoryRouter>
        <PayableTable
          companyId='company_1'
          stageLabel='Entrada'
          items={[]}
          total={0}
          page={1}
          totalPages={1}
          onPage={() => {}}
        />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Entrada/)).toBeInTheDocument();
  });
});
