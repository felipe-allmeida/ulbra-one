import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseMe, mockUseRequisitions, mockUseStageCounts } = vi.hoisted(() => ({
  mockUseMe: vi.fn(),
  mockUseRequisitions: vi.fn(),
  mockUseStageCounts: vi.fn(),
}));

vi.mock('../../store', () => ({
  useApp: () => ({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' }),
}));
vi.mock('../../queries', () => ({
  useMe: mockUseMe,
  useRoles: () => ({ data: [] }),
  useRequisitions: mockUseRequisitions,
  useRequisitionStageCounts: mockUseStageCounts,
  useCostCenterBudgetSummary: () => ({ data: [] }),
}));

import { RequesterDashboard } from './requester-home';

const pageOf = (items: unknown[]) => ({ data: { items, total: items.length, totalPages: 1, page: 1 } });

beforeEach(() => {
  mockUseMe.mockReturnValue({ data: { isAdmin: false, permissions: ['requisitions:write'] } });
  mockUseStageCounts.mockReturnValue({ counts: { Paid: 5, Finalized: 2 }, totals: {}, isLoading: false });
  // The sections now issue ONE multi-status query each (params.statuses); key off the set (tolerate the old
  // singular status too) so a Complement item lands in "Precisa de você" and a Triage item in "Acompanhando".
  mockUseRequisitions.mockImplementation((params: { status?: string; statuses?: string[] }) => {
    const set = params.statuses ?? (params.status ? [params.status] : []);
    if (set.includes('Complement'))
      return pageOf([
        {
          id: 'r1',
          code: 1041,
          description: 'Livraria Saber',
          type: 'Purchase',
          status: 'Complement',
          estimateValue: 2650,
        },
      ]);
    if (set.includes('Triage'))
      return pageOf([
        {
          id: 'r2',
          code: 1043,
          description: 'TI Soluções',
          type: 'ServiceProvision',
          status: 'Triage',
          estimateValue: 18900,
        },
      ]);
    return pageOf([]);
  });
});

describe('RequesterDashboard', () => {
  it('renders the three grouped sections + aside and lists a returned requisition', () => {
    render(
      <MemoryRouter initialEntries={['/app/requisitions/mine']}>
        <RequesterDashboard />
      </MemoryRouter>,
    );
    expect(screen.getByText('Acompanhando')).toBeInTheDocument();
    expect(screen.getByText('Sua fila')).toBeInTheDocument();
    expect(screen.getByText(/1041/)).toBeInTheDocument();
    // stage counts (done tile) are scoped to the user's own requisitions
    expect(mockUseStageCounts).toHaveBeenCalledWith('company_1', expect.any(Array), { mine: true });
  });

  it('shows "Nova requisição" with write permission', () => {
    render(
      <MemoryRouter initialEntries={['/app/requisitions/mine']}>
        <RequesterDashboard />
      </MemoryRouter>,
    );
    expect(screen.getByText('Nova requisição')).toBeInTheDocument();
  });

  it('paginates a section (pip = total do servidor + Pager navega, sem capar linhas)', () => {
    // stage-counts diz 137 em "Precisa de você"; a página só traz 1 linha — o pip NÃO pode virar 1.
    mockUseStageCounts.mockReturnValue({ counts: { Complement: 137 }, totals: {}, isLoading: false });
    mockUseRequisitions.mockImplementation((params: { status?: string; statuses?: string[] }) => {
      const set = params.statuses ?? (params.status ? [params.status] : []);
      if (set.includes('Complement'))
        return {
          data: {
            items: [
              {
                id: 'r1',
                code: 1041,
                description: 'Livraria Saber',
                type: 'Purchase',
                status: 'Complement',
                estimateValue: 2650,
              },
            ],
            total: 137,
            totalPages: 7,
            page: 1,
          },
        };
      return pageOf([]);
    });

    render(
      <MemoryRouter initialEntries={['/app/requisitions/mine']}>
        <RequesterDashboard />
      </MemoryRouter>,
    );

    // Pip = total do servidor (137), não a única linha da página. Aparece na seção E no tile da aside.
    expect(screen.getAllByText('137').length).toBeGreaterThan(0);
    // Pager visível (mais de uma página) e navegável.
    expect(screen.getByText('Página 1 de 7')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }));
    expect(mockUseRequisitions).toHaveBeenCalledWith(
      expect.objectContaining({ statuses: expect.arrayContaining(['Complement']), page: 2 }),
      expect.anything(),
    );
  });
});
