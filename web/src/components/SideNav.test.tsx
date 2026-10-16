import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';
import { setViewportMatches } from '../test/setup';

const { mockUseMe, navigateSpy } = vi.hoisted(() => ({ mockUseMe: vi.fn(), navigateSpy: vi.fn() }));
vi.mock('../queries', () => ({
  useMe: mockUseMe,
  useRequisitions: () => ({ data: { items: [], total: 4, totalPages: 1 } }),
  usePayableStageItems: () => ({ data: undefined }),
}));
vi.mock('../store', () => ({ useApp: () => ({ empresaId: 'company_1' }) }));
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigateSpy };
});

import { SideNav } from './SideNav';
import { SidebarLayoutProvider } from '../sidebar-layout';

beforeEach(() => {
  navigateSpy.mockClear();
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
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    // What the Requester sees under Operation
    expect(screen.getByText('Minhas solicitações')).toBeInTheDocument();
    expect(screen.getByText('Solicitações')).toBeInTheDocument();
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
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    // Role-restricted rail: no Cadastros/Estrutura (audience notFinalApprover). "Início" is gone for
    // every role now — kept here as a guard against the domain coming back.
    expect(screen.queryByText('Início')).not.toBeInTheDocument();
    expect(screen.queryByText('Cadastros')).not.toBeInTheDocument();
    expect(screen.queryByText('Estrutura')).not.toBeInTheDocument();
    // Budget domain panel: the role's 3 screens, without the full list.
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Aprovação')).toBeInTheDocument();
    expect(screen.getByText('Orçamentos')).toBeInTheDocument();
    expect(screen.queryByText('Solicitações')).not.toBeInTheDocument();
    expect(screen.queryByText('Minhas solicitações')).not.toBeInTheDocument();
    expect(screen.queryByText('Adequações')).not.toBeInTheDocument();
    // Queue badge (mock ib/rdot): total=4 on the Aprovação item and on the domain rail.
    expect(screen.getAllByText('4').length).toBeGreaterThanOrEqual(1);
  });
});

describe('SideNav — mobile two-level menu (phone, ≤640px)', () => {
  beforeEach(() => {
    setViewportMatches(true);
    // Admin: every domain/item visible — lets the level-1/level-2/search assertions below
    // exercise more than one domain without fighting per-role permission filtering.
    mockUseMe.mockReturnValue({
      data: { isAdmin: true, name: 'Ana Torres', role: 'Admin', permissions: [] },
    });
  });

  it('lists the domains at level 1, not the 46 items', () => {
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    expect(screen.getByText('Financeiro')).toBeInTheDocument();
    expect(screen.getByText('Contábil')).toBeInTheDocument();
    // Items several levels deep must not leak into the domain list.
    expect(screen.queryByText('Plano de Contas')).not.toBeInTheDocument();
    expect(screen.queryByText('Fluxo de caixa')).not.toBeInTheDocument();
  });

  it('tapping a domain shows that domain’s items and hides the domain list', () => {
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Contábil'));
    expect(screen.getByText('Plano de Contas')).toBeInTheDocument();
    expect(screen.queryByText('Financeiro')).not.toBeInTheDocument();
  });

  it('the back control returns to level 1', () => {
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Contábil'));
    expect(screen.getByText('Plano de Contas')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Voltar' }));
    expect(screen.getByText('Financeiro')).toBeInTheDocument();
    expect(screen.queryByText('Plano de Contas')).not.toBeInTheDocument();
  });

  it('every domain row always shows a chevron and drills in (no more direct-navigate domain since Início was removed)', () => {
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    const financeiroRow = screen.getByText('Financeiro').closest('button')!;
    expect(financeiroRow.querySelector('.mnav-row-chevron')).toBeInTheDocument();
    fireEvent.click(financeiroRow);
    expect(screen.getByRole('button', { name: 'Voltar' })).toBeInTheDocument();
  });

  it('typing in search shows results from more than one domain and navigates on selection', () => {
    const onClose = vi.fn();
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={onClose} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    fireEvent.change(screen.getByPlaceholderText('Buscar…'), { target: { value: 'conta' } });
    // "conta" matches items in more than one domain: Financeiro (Contas a pagar) and Contábil (Plano de Contas).
    expect(screen.getByText('Contas a pagar')).toBeInTheDocument();
    expect(screen.getByText('Plano de Contas')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Contas a pagar'));
    expect(navigateSpy).toHaveBeenCalledWith('/app/payables');
    expect(onClose).toHaveBeenCalled();
  });

  it('the drawer closes after navigating from a domain panel item', () => {
    const onClose = vi.fn();
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={onClose} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Contábil'));
    fireEvent.click(screen.getByText('Plano de Contas'));
    expect(navigateSpy).toHaveBeenCalledWith('/app/accounting/chart-of-accounts');
    expect(onClose).toHaveBeenCalled();
  });
});

describe('SideNav — desktop/tablet unaffected (>640px)', () => {
  it('renders the rail + panel exactly as before at desktop width', () => {
    setViewportMatches(false);
    mockUseMe.mockReturnValue({
      data: { isAdmin: true, name: 'Ana Torres', role: 'Admin', permissions: [] },
    });
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    expect(document.querySelector('.nav-rail')).toBeInTheDocument();
    expect(document.querySelector('.nav-panel')).toBeInTheDocument();
    expect(document.querySelector('.mnav')).not.toBeInTheDocument();
  });
});

describe('SideNav — alça de redimensionar (desktop)', () => {
  const renderNav = () => {
    setViewportMatches(false);
    return render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
  };

  const handle = () => screen.getByRole('separator');
  const aside = () => document.querySelector('.side-nav')!;

  beforeEach(() => window.localStorage.clear());

  it('sem preferência salva, o painel abre nos 225px de hoje', () => {
    renderNav();
    expect(aside()).not.toHaveClass('nav-collapsed');
    expect(aside().getAttribute('style')).toContain('--nav-panel-w: 225px');
  });

  it('a alça é um separador operável por teclado, com a faixa anunciada', () => {
    renderNav();
    const h = handle();
    expect(h).toHaveAttribute('aria-orientation', 'vertical');
    expect(h).toHaveAttribute('aria-valuemin', '180');
    expect(h).toHaveAttribute('aria-valuemax', '420');
    expect(h).toHaveAttribute('aria-valuenow', '225');
    expect(h).toHaveAttribute('tabindex', '0');
  });

  it('setas movem em passos e respeitam os limites', () => {
    renderNav();
    fireEvent.keyDown(handle(), { key: 'ArrowRight' });
    expect(aside().getAttribute('style')).toContain('--nav-panel-w: 241px');
    fireEvent.keyDown(handle(), { key: 'ArrowLeft' });
    expect(aside().getAttribute('style')).toContain('--nav-panel-w: 225px');

    fireEvent.keyDown(handle(), { key: 'End' });
    expect(aside().getAttribute('style')).toContain('--nav-panel-w: 420px');
    fireEvent.keyDown(handle(), { key: 'Home' });
    expect(aside().getAttribute('style')).toContain('--nav-panel-w: 180px');
  });

  it('seta para a esquerda no mínimo recolhe, em vez de travar na borda', () => {
    renderNav();
    fireEvent.keyDown(handle(), { key: 'Home' });
    expect(aside()).not.toHaveClass('nav-collapsed');
    fireEvent.keyDown(handle(), { key: 'ArrowLeft' });
    expect(aside()).toHaveClass('nav-collapsed');
  });

  it('a preferência sobrevive à remontagem', () => {
    const first = renderNav();
    fireEvent.keyDown(handle(), { key: 'ArrowRight' });
    first.unmount();

    renderNav();
    expect(aside().getAttribute('style')).toContain('--nav-panel-w: 241px');
  });

  it('o gatilho de recolher NÃO vive aqui — ele é chrome da topbar (ver Shell.test.tsx)', () => {
    renderNav();
    expect(document.querySelector('.nav-collapse-btn')).not.toBeInTheDocument();
  });

  it('no telefone não existe alça — a folha é outra coisa', () => {
    setViewportMatches(true);
    render(
      <MemoryRouter initialEntries={['/app/payables']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    expect(screen.queryByRole('separator')).not.toBeInTheDocument();
  });
});

describe('SideNav — sem o domínio Início', () => {
  it('rota fora do mapa da nav: cai no primeiro domínio VISÍVEL, não num painel vazio', () => {
    // /app/notificacoes não pertence a nenhum item da nav; `domainForPath` cai no primeiro domínio
    // (Financeiro), que o Requester não enxerga — sem o fallback por visibilidade o painel viria vazio.
    render(
      <MemoryRouter initialEntries={['/app/notificacoes']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    expect(screen.getByText('Minhas solicitações')).toBeInTheDocument();
    expect(screen.queryByText('Sem itens neste módulo para o seu acesso.')).not.toBeInTheDocument();
  });

  it('o painel mostra só o domínio selecionado, nunca o índice do menu inteiro', () => {
    render(
      <MemoryRouter initialEntries={['/app/requisitions/mine']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    expect(screen.getByText('Minhas solicitações')).toBeInTheDocument();
    // Itens de outros domínios que o Requester enxerga (Cadastros/Estrutura) não podem vazar no painel.
    expect(screen.queryByText('Fornecedores')).not.toBeInTheDocument();
    expect(screen.queryByText('Empresas e unidades')).not.toBeInTheDocument();
  });
});
