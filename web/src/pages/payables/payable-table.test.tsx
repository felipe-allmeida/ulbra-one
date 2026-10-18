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
  it('renders rows and navigates to detail on row click, stamping the origin url', () => {
    render(
      <MemoryRouter initialEntries={['/app/payables?stage=approval']}>
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
    // The row records where the user came from so the detail's back button can return here.
    expect(navigate).toHaveBeenCalledWith('/app/payables/payable_1', {
      state: { from: '/app/payables?stage=approval' },
    });
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
  /**
   * A célula de status era zona morta: `stopPropagation` na célula inteira matava a navegação da
   * linha sem pôr nada no lugar. A exceção tem que ser o controle, não a célula.
   */
  it('opens the detail when the click lands on the status cell', () => {
    navigate.mockClear();
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <PayableTable
          companyId='company_1'
          stageLabel='Entrada'
          items={items}
          total={1}
          page={1}
          totalPages={1}
          onPage={() => {}}
          statusCell={() => <span>Atrasado</span>}
        />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByText('Atrasado'));

    expect(navigate).toHaveBeenCalledWith('/app/payables/payable_1', {
      state: { from: '/app/payables' },
      replace: undefined,
    });
  });

  it('does not open the detail when the click lands on the action column button', () => {
    navigate.mockClear();
    const onAction = vi.fn();
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <PayableTable
          companyId='company_1'
          stageLabel='Entrada'
          items={items}
          total={1}
          page={1}
          totalPages={1}
          onPage={() => {}}
          extraColumn={{
            header: 'Ação',
            cell: () => (
              <button type='button' onClick={onAction}>
                Aprovar
              </button>
            ),
          }}
        />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Aprovar' }));

    expect(onAction).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('showNet renders gross + net columns; the net cell stays "—" until the withholdings lock', () => {
    const mixed = [
      { ...items[0], payableId: 'p_paid', status: 'Paid', totalAmount: 1000, netTotal: 940 },
      { ...items[0], payableId: 'p_received', status: 'Received', totalAmount: 500, netTotal: 500 },
    ] as any;
    render(
      <MemoryRouter>
        <PayableTable
          companyId='company_1'
          stageLabel='Conciliado'
          items={mixed}
          total={2}
          page={1}
          totalPages={1}
          onPage={() => {}}
          showNet
        />
      </MemoryRouter>,
    );
    expect(screen.getByText('Valor bruto')).toBeInTheDocument();
    expect(screen.getByText('Valor líquido')).toBeInTheDocument();
    expect(screen.getByText(/R\$\s?940/)).toBeInTheDocument(); // Paid → net shown
    expect(screen.getByText('—')).toBeInTheDocument(); // Received → net not locked yet
  });

  it('without showNet the single "Valor" column renders and no net column exists', () => {
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
    expect(screen.getByText('Valor')).toBeInTheDocument();
    expect(screen.queryByText('Valor líquido')).toBeNull();
    expect(screen.queryByText('Valor bruto')).toBeNull();
  });

  it('tags advance rows with "Adiantamento" in the default status cell', () => {
    const mixed = [
      { ...items[0], payableId: 'p_advance', origin: 'Advance' },
      { ...items[0], payableId: 'p_manual', origin: 'Manual' },
    ] as any;
    render(
      <MemoryRouter>
        <PayableTable
          companyId='company_1'
          stageLabel='Entrada'
          items={mixed}
          total={2}
          page={1}
          totalPages={1}
          onPage={() => {}}
        />
      </MemoryRouter>,
    );
    expect(screen.getAllByText('Adiantamento')).toHaveLength(1);
  });

  it('shows the "Nota Fiscal pendente" tag only on PendingAccounting rows lacking a fiscal document', () => {
    const accItems = [
      { ...items[0], payableId: 'p_no_nf', status: 'PendingAccounting', hasFiscalDocument: false },
      { ...items[0], payableId: 'p_with_nf', status: 'PendingAccounting', hasFiscalDocument: true },
    ] as any;
    render(
      <MemoryRouter>
        <PayableTable
          companyId='company_1'
          stageLabel='Contabilidade'
          items={accItems}
          total={2}
          page={1}
          totalPages={1}
          onPage={() => {}}
        />
      </MemoryRouter>,
    );
    expect(screen.getAllByText('Nota Fiscal pendente')).toHaveLength(1);
  });

  it('flags Scheduled/Paid rows with no NF as "Nota Fiscal pendente" (auto-scheduled requisitions), excluding advances', () => {
    // AEL-846: requisition payables schedule/pay without an NF, so the pendency must persist past accounting.
    const rows = [
      { ...items[0], payableId: 'p_sched_no_nf', status: 'Scheduled', hasFiscalDocument: false },
      { ...items[0], payableId: 'p_paid_no_nf', status: 'Paid', hasFiscalDocument: false },
      { ...items[0], payableId: 'p_sched_with_nf', status: 'Scheduled', hasFiscalDocument: true },
      { ...items[0], payableId: 'p_advance_no_nf', status: 'Scheduled', hasFiscalDocument: false, origin: 'Advance' },
    ] as any;
    render(
      <MemoryRouter>
        <PayableTable
          companyId='company_1'
          stageLabel='Pagamento'
          items={rows}
          total={4}
          page={1}
          totalPages={1}
          onPage={() => {}}
        />
      </MemoryRouter>,
    );
    // Two rows tagged: the Scheduled + Paid with no NF. The with-NF and the Advance rows are not.
    expect(screen.getAllByText('Nota Fiscal pendente')).toHaveLength(2);
  });
});

describe('PayableTable no telefone', () => {
  beforeEach(() => setViewportMatches(true));

  const setup = () =>
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

  it('mostra descrição e valor no cabeçalho do card', () => {
    setup();
    expect(screen.getByText('NF 100')).toBeInTheDocument();
    expect(screen.getByText(/R\$\s?3\.702/)).toBeInTheDocument();
    expect(document.querySelector('table')).not.toBeInTheDocument();
  });

  it('revela natureza orçamentária ao expandir', async () => {
    setup();
    expect(screen.queryByText('Marketing')).not.toBeInTheDocument();
    await userEvent.click(screen.getAllByRole('button', { name: /expandir/i })[0]);
    expect(screen.getByText('Natureza orçamentária')).toBeInTheDocument();
    expect(screen.getByText('Marketing')).toBeInTheDocument();
  });
});
