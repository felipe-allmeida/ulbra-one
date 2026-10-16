import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';
import { setViewportMatches } from '../test/setup';

const { mockUseMe } = vi.hoisted(() => ({ mockUseMe: vi.fn() }));
vi.mock('../queries', () => ({
  useMe: mockUseMe,
  useRequisitions: () => ({ data: { items: [], total: 0, totalPages: 1 } }),
  usePayableStageItems: () => ({ data: undefined }),
}));
vi.mock('../store', () => ({ useApp: () => ({ empresaId: 'company_1' }) }));

/**
 * `nav.config.tsx` documents that every domain's `requires` is the union of its items'
 * `requires` — by design, no real role today hits "domain visible, every item filtered
 * out". This synthetic single-domain config forces exactly that (domain has no gate,
 * its one item requires `admin:users`, which the test persona lacks), to exercise the
 * defensive empty-state render at level 2 on mobile.
 */
vi.mock('../nav.config', async (orig) => {
  const actual = await orig<typeof import('../nav.config')>();
  const ghost = {
    key: 'ghost',
    icon: actual.NAV_DOMAINS[0].icon,
    labelKey: 'nav.domains.settings',
    groups: [
      {
        items: [
          { labelKey: 'nav.audit', to: '/app/ghost-item', requires: ['admin:users'], icon: actual.NAV_DOMAINS[0].icon },
        ],
      },
    ],
  };
  return { ...actual, NAV_DOMAINS: [ghost] };
});

import { SideNav } from './SideNav';
import { SidebarLayoutProvider } from '../sidebar-layout';

describe('SideNav — domain visible with zero visible items (mobile level 2)', () => {
  it('shows the empty-domain message instead of a blank panel', () => {
    setViewportMatches(true);
    mockUseMe.mockReturnValue({
      data: { isAdmin: false, name: 'Sol', role: 'Requester', permissions: [] },
    });
    render(
      <MemoryRouter initialEntries={['/app/ghost']}>
        <SidebarLayoutProvider>
          <SideNav open onClose={() => {}} />
        </SidebarLayoutProvider>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Config'));
    expect(screen.getByText('Sem itens neste módulo para o seu acesso.')).toBeInTheDocument();
  });
});
