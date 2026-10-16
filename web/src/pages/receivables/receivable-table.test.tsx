import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';
import { setViewportMatches } from '../../test/setup';

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
  it('renders rows and navigates to detail on row click, stamping the origin url', () => {
    render(
      <MemoryRouter initialEntries={['/app/receivables?stage=reconciled']}>
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
    expect(navigate).toHaveBeenCalledWith('/app/receivables/rcv_1', {
      state: { from: '/app/receivables?stage=reconciled' },
    });
  });
  /**
   * As células de status e de ação paravam a propagação inteiras — o botão ficava isolado, mas todo
   * o resto da célula virava zona morta. A exceção tem que ser o controle, não a célula.
   */
  it('opens the detail when the click lands on the action column outside a control', () => {
    navigate.mockClear();
    render(
      <MemoryRouter initialEntries={['/app/receivables']}>
        <ReceivableTable
          companyId='company_1'
          stageLabel='A receber'
          items={items}
          total={1}
          page={1}
          totalPages={1}
          onPage={() => {}}
          extraColumn={{ header: 'Situação', cell: () => <span>Em aberto</span> }}
        />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByText('Em aberto'));

    expect(navigate).toHaveBeenCalledWith('/app/receivables/rcv_1', {
      state: { from: '/app/receivables' },
      replace: undefined,
    });
  });

  it('does not open the detail when the click lands on the action column button', () => {
    navigate.mockClear();
    const onAction = vi.fn();
    render(
      <MemoryRouter initialEntries={['/app/receivables']}>
        <ReceivableTable
          companyId='company_1'
          stageLabel='A receber'
          items={items}
          total={1}
          page={1}
          totalPages={1}
          onPage={() => {}}
          extraColumn={{
            header: 'Ação',
            cell: () => (
              <button type='button' onClick={onAction}>
                Baixar
              </button>
            ),
          }}
        />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Baixar' }));

    expect(onAction).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalled();
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

describe('ReceivableTable no telefone', () => {
  beforeEach(() => setViewportMatches(true));

  const setup = () =>
    render(
      <MemoryRouter>
        <ReceivableTable
          companyId='company_1'
          stageLabel='A receber'
          items={items}
          total={1}
          page={1}
          totalPages={1}
          onPage={vi.fn()}
        />
      </MemoryRouter>,
    );

  it('mostra descrição e valor no cabeçalho do card', () => {
    setup();
    expect(screen.getByText('Mensalidade')).toBeInTheDocument();
    expect(screen.getByText(/R\$\s?1\.000/)).toBeInTheDocument();
    expect(document.querySelector('table')).not.toBeInTheDocument();
  });

  it('revela centro de custo ao expandir', async () => {
    setup();
    expect(screen.queryByText('Vendas')).not.toBeInTheDocument();
    await userEvent.click(screen.getAllByRole('button', { name: /expandir/i })[0]);
    expect(screen.getByText('Vendas')).toBeInTheDocument();
  });
});
