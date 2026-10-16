import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const navigate = vi.fn();
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigate };
});
vi.mock('../../store', async (orig) => {
  const actual = await orig<typeof import('../../store')>();
  return { ...actual, useApp: () => ({ empresas: [] }) };
});
vi.mock('../../queries', () => ({
  useUnits: () => ({ data: [{ id: 'unit_1', acronym: 'ULBRA/CAN' }] }),
  useCostCenters: () => ({ data: [{ id: 'cc_1', name: 'Marketing' }] }),
}));

import { RequisitionTable } from './requisition-table';

const items = [
  {
    id: 'req_1',
    code: 2418,
    companyId: 'company_1',
    unitId: 'unit_1',
    costCenterId: 'cc_1',
    vendorName: 'Papelaria Central',
    type: 'Service',
    estimateValue: 4280,
    estimateType: 'AVista',
    startCompetency: '2026-03',
    status: 'Triage',
    statusChangedAt: '2026-08-27T12:00:00Z',
  },
] as never[];

function renderTable(extraColumn?: { header: string; cell: () => React.ReactNode }) {
  return render(
    <MemoryRouter initialEntries={['/app/requisitions?stage=triagem']}>
      <RequisitionTable
        companyId='company_1'
        title='Triagem'
        items={items}
        total={1}
        page={1}
        totalPages={1}
        onPage={() => undefined}
        extraColumn={extraColumn}
      />
    </MemoryRouter>,
  );
}

describe('RequisitionTable', () => {
  beforeEach(() => navigate.mockClear());

  it('abre o detalhe quando o clique cai no nome do fornecedor', () => {
    renderTable();

    fireEvent.click(screen.getByText('Papelaria Central'));

    expect(navigate).toHaveBeenCalledWith('/app/requisitions/req_1', {
      state: { from: '/app/requisitions?stage=triagem' },
      replace: undefined,
    });
  });

  /**
   * A célula de status era a maior zona morta da tela: `stopPropagation` matava a navegação da
   * linha e não havia handler nenhum no lugar. O badge é o alvo mais visível da linha — é
   * justamente onde o usuário mira.
   */
  it('abre o detalhe quando o clique cai no badge de status', () => {
    renderTable();

    fireEvent.click(screen.getByText('Triagem', { selector: '.badge-status' }));

    expect(navigate).toHaveBeenCalledWith('/app/requisitions/req_1', {
      state: { from: '/app/requisitions?stage=triagem' },
      replace: undefined,
    });
  });

  it('não abre o detalhe quando o clique cai no botão da coluna de ação', () => {
    const onAction = vi.fn();
    renderTable({
      header: 'Ação',
      cell: () => (
        <button type='button' onClick={onAction}>
          Encaminhar
        </button>
      ),
    });

    fireEvent.click(screen.getByRole('button', { name: 'Encaminhar' }));

    expect(onAction).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalled();
  });
});
