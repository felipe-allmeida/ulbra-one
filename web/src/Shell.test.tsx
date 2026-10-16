import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import './i18n';
import { setViewportMatches } from './test/setup';

// Same mocking shape as SideNav.test.tsx (queries + store), plus the extra hooks Topbar itself
// needs (impersonation + theme) now that the hamburger's new home (Shell) renders both
// SideNav and Topbar side by side.
const { mockUseMe, navigateSpy } = vi.hoisted(() => ({ mockUseMe: vi.fn(), navigateSpy: vi.fn() }));
vi.mock('./queries', () => ({
  useMe: mockUseMe,
  useRoles: () => ({ data: [] }),
  useRequisitions: () => ({ data: { items: [], total: 0, totalPages: 1 } }),
  usePayableStageItems: () => ({ data: undefined }),
  useUnreadNotificationsCount: () => ({ data: { count: 0 } }),
}));
vi.mock('./store', () => ({
  useApp: () => ({
    empresaId: 'company_1',
    empresas: [],
    empresa: null,
    isAllCompanies: false,
    canSeeAllCompanies: false,
    setEmpresaId: vi.fn(),
  }),
}));
vi.mock('./impersonation', () => ({ useImpersonation: () => ({ asRole: null, setAsRole: vi.fn() }) }));
vi.mock('./theme', () => ({ useTheme: () => ({ theme: 'light', toggle: vi.fn() }) }));
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigateSpy };
});

import { Shell } from './app-layout';

function renderShell(initialPath = '/app/payables') {
  const qc = new QueryClient();
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Shell>
          <div>page content</div>
        </Shell>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  navigateSpy.mockClear();
  mockUseMe.mockReturnValue({
    data: { isAdmin: true, name: 'Ana Torres', role: 'Admin', permissions: [] },
  });
});

describe('Shell — gatilho de recolher a sidebar vive na topbar, não na sidebar', () => {
  beforeEach(() => window.localStorage.clear());

  it('renderiza no canto superior esquerdo, dentro da topbar e antes do breadcrumb', () => {
    setViewportMatches(false);
    renderShell();
    const btn = document.querySelector('.nav-collapse-btn')!;
    const topbarMenu = document.querySelector('.topbar-menu')!;
    expect(topbarMenu).toContainElement(btn as HTMLElement);
    // Antes do breadcrumb: é o primeiro filho, como em Linear/Azure/Claude Desktop.
    expect(topbarMenu.firstElementChild).toBe(btn);
    // E fora da sidebar: preso a ela, sumiria junto ao recolher.
    expect(document.querySelector('.side-nav')!.contains(btn)).toBe(false);
  });

  it('recolhe e expande a sidebar, com o nome acessível acompanhando o estado', () => {
    setViewportMatches(false);
    renderShell();
    const aside = () => document.querySelector('.side-nav')!;

    fireEvent.click(screen.getByRole('button', { name: 'Recolher menu' }));
    expect(aside()).toHaveClass('nav-collapsed');

    fireEvent.click(screen.getByRole('button', { name: 'Expandir menu' }));
    expect(aside()).not.toHaveClass('nav-collapsed');
  });

  it('continua alcançável com a sidebar recolhida — é chrome do app, não da sidebar', () => {
    setViewportMatches(false);
    renderShell();
    fireEvent.click(screen.getByRole('button', { name: 'Recolher menu' }));
    expect(screen.getByRole('button', { name: 'Expandir menu' })).toBeInTheDocument();
  });

  it('o rail sobrevive ao recolher — trocar de domínio não exige expandir', () => {
    setViewportMatches(false);
    renderShell();
    fireEvent.click(screen.getByRole('button', { name: 'Recolher menu' }));
    expect(document.querySelector('.nav-rail')).toBeInTheDocument();
    expect(document.querySelectorAll('.nav-rail-btn').length).toBeGreaterThan(1);
  });

  it('não se confunde com o `.hamburger`: são dois controles distintos', () => {
    setViewportMatches(false);
    renderShell();
    expect(document.querySelectorAll('.hamburger').length).toBe(1);
    expect(document.querySelector('.nav-collapse-btn')).not.toHaveClass('hamburger');
  });
});

describe('Shell — nav trigger lives outside the topbar (single .hamburger)', () => {
  it('exactly one .hamburger exists in the DOM', () => {
    renderShell();
    expect(document.querySelectorAll('.hamburger').length).toBe(1);
  });

  it('at phone width (≤640px), the trigger is not inside the topbar and opens the menu', () => {
    setViewportMatches(true);
    renderShell();
    const topbar = document.querySelector('.topbar')!;
    expect(topbar.querySelector('.hamburger')).not.toBeInTheDocument();
    expect(document.querySelectorAll('.hamburger').length).toBe(1);

    fireEvent.click(document.querySelector('.hamburger')!);
    expect(document.querySelector('.side-nav')).toHaveClass('open');
  });

  it('at 768px (641–1024px band), the trigger is still outside the topbar’s DOM (CSS puts it back in that spot) and still opens the menu', () => {
    // This project's viewport mock is a single boolean shared by every matchMedia query
    // (see src/test/setup.ts), so it cannot distinguish "tablet" from "desktop" in JS — by design,
    // this project only branches in JS on phone vs. not-phone. The 641–1024px visual position
    // (the trigger sitting back where the topbar used to render it) is CSS-only and is verified
    // by eye/e2e, not by this jsdom test. What IS testable here: nothing regressed structurally
    // or behaviorally for the non-phone band.
    setViewportMatches(false);
    renderShell();
    expect(document.querySelectorAll('.hamburger').length).toBe(1);

    fireEvent.click(document.querySelector('.hamburger')!);
    expect(document.querySelector('.side-nav')).toHaveClass('open');
  });

  it('the topbar keeps the breadcrumb after losing the trigger', () => {
    renderShell('/app/payables');
    const topbar = document.querySelector('.topbar')!;
    expect(topbar.querySelector('.crumb')).toBeInTheDocument();
  });

  it('the menu closes on navigation (clicking a nav item closes the drawer opened via the moved trigger)', () => {
    setViewportMatches(true);
    renderShell('/app/payables');
    fireEvent.click(document.querySelector('.hamburger')!);
    expect(document.querySelector('.side-nav')).toHaveClass('open');

    // getByRole, not getByText: at '/app/payables' the breadcrumb itself reads "Financeiro / Contas a
    // pagar", so a plain text match is ambiguous between the crumb and the domain row button.
    fireEvent.click(screen.getByRole('button', { name: 'Financeiro' }));
    fireEvent.click(screen.getByText('Contas a pagar'));
    expect(navigateSpy).toHaveBeenCalledWith('/app/payables');
    expect(document.querySelector('.side-nav')).not.toHaveClass('open');
  });
});
