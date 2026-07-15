import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const navigate = vi.fn();
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigate };
});
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: { cc_1: 'Vendas' } }) }));
vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof import('../../permissions')>()),
  usePermissions: () => ({ me: null, isAdmin: true, can: () => true, canAny: () => true }),
}));

import { ReceivableTable } from './receivable-table';

const items = [
  {
    receivableId: 'rcv_1',
    payerName: 'Cliente A',
    description: 'Mensalidade',
    costCenterId: 'cc_1',
    firstDueDate: '2026-06-30',
    installmentCount: 2,
    totalAmount: 1000,
    status: 'AwaitingReceipt',
  },
] as any;

describe('ReceivableTable', () => {
  it('renders rows and navigates to detail on row click', () => {
    render(
      <MemoryRouter>
        <ReceivableTable
          companyId='company_1'
          stageLabel='A receber'
          items={items}
          total={1}
          page={1}
          totalPages={1}
          onPage={() => {}}
        />
      </MemoryRouter>,
    );
    expect(screen.getByText('Mensalidade')).toBeInTheDocument();
    expect(screen.getByText('Cliente A')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Mensalidade'));
    expect(navigate).toHaveBeenCalledWith('/app/receivables/rcv_1');
  });
  it('shows an empty state when there are no items', () => {
    render(
      <MemoryRouter>
        <ReceivableTable
          companyId='company_1'
          stageLabel='Conciliado'
          items={[]}
          total={0}
          page={1}
          totalPages={1}
          onPage={() => {}}
        />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Conciliado/)).toBeInTheDocument();
  });
});
