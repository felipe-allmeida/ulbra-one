import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';

const { mockUseMe } = vi.hoisted(() => ({ mockUseMe: vi.fn() }));
vi.mock('../queries', () => ({
  useMe: mockUseMe,
  useRequisitions: () => ({ data: { items: [], total: 4, totalPages: 1 } }),
  usePayableStageItems: () => ({ data: undefined }),
}));
vi.mock('../store', () => ({ useApp: () => ({ empresaId: 'company_1' }) }));

import { SideNav } from './SideNav';

beforeEach(() => {
  // Requester: creates/tracks requisitions; no budget/adjustments access (the "Planejamento" subgroup).
  mockUseMe.mockReturnValue({
    data: {
      isAdmin: false,
      name: 'Sol',
      role: 'Requester',
      permissions: ['requisitions:read', 'requisitions:write', 'core:read', 'registry:read', 'projects:read'],
    },
  });
});

describe('SideNav — subgroups by role', () => {
  it('Requester: the Budget panel does not render the empty "Planejamento" subgroup', () => {
    render(
      <MemoryRouter initialEntries={['/app/requisitions/mine']}>
        <SideNav open onClose={() => {}} />
      </MemoryRouter>,
    );
    // What the Requester sees under Operation
    expect(screen.getByText('Minhas requisições')).toBeInTheDocument();
    expect(screen.getByText('Requisições')).toBeInTheDocument();
    // "Planejamento" only holds Adjustments (which the Requester can't access) → the header must not stand alone
    expect(screen.queryByText('Planejamento')).not.toBeInTheDocument();
    expect(screen.queryByText('Adequações')).not.toBeInTheDocument();
    // and Triage (requisitions:triage) stays out too
    expect(screen.queryByText('Triagem')).not.toBeInTheDocument();
    // Final approver screens do not leak to the Requester (requires approve)
    expect(screen.queryByText('Aprovação')).not.toBeInTheDocument();
  });

  it('Aprovador (aprovador final): rail só com Orçamento; painel = Dashboard/Aprovação/Orçamentos + badge', () => {
    mockUseMe.mockReturnValue({
      data: {
        isAdmin: false,
        name: 'Carlos',
        role: 'Approver',
        permissions: ['requisitions:read', 'requisitions:approve', 'core:read', 'registry:read', 'projects:read'],
      },
    });
    render(
      <MemoryRouter initialEntries={['/app/requisitions/approval']}>
        <SideNav open onClose={() => {}} />
      </MemoryRouter>,
    );
    // Role-restricted rail: no Início/Cadastros/Estrutura (audience notFinalApprover).
    expect(screen.queryByText('Início')).not.toBeInTheDocument();
    expect(screen.queryByText('Cadastros')).not.toBeInTheDocument();
    expect(screen.queryByText('Estrutura')).not.toBeInTheDocument();
    // Budget domain panel: the role's 3 screens, without the full list.
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Aprovação')).toBeInTheDocument();
    expect(screen.getByText('Orçamentos')).toBeInTheDocument();
    expect(screen.queryByText('Requisições')).not.toBeInTheDocument();
    expect(screen.queryByText('Minhas requisições')).not.toBeInTheDocument();
    expect(screen.queryByText('Adequações')).not.toBeInTheDocument();
    // Queue badge (mock ib/rdot): total=4 on the Aprovação item and on the domain rail.
    expect(screen.getAllByText('4').length).toBeGreaterThanOrEqual(1);
  });
});
