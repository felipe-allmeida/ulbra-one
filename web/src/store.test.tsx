import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const { mockUseCompanies, mockUseMe } = vi.hoisted(() => ({
  mockUseCompanies: vi.fn(),
  mockUseMe: vi.fn(),
}));

vi.mock('./queries', () => ({
  ALL_COMPANIES: '__all__',
  useCompanies: mockUseCompanies,
  useMe: mockUseMe,
  queryKeys: { companies: ['companies'] as const },
}));

import { AppDataProvider, ALL_COMPANIES, useApp } from './store';

const COMPANIES = [
  { id: 'company_ulbra', legalName: 'Ulbra S.A', cnpj: '1', active: true },
  { id: 'company_celsp', legalName: 'CELSP', cnpj: '2', active: true },
];

function Probe() {
  const { empresas, empresaId, isAllCompanies, scopeCompanyId, canSeeAllCompanies } = useApp();
  return (
    <>
      <div data-testid='ids'>{empresas.map((e) => e.id).join(',')}</div>
      <div data-testid='sel'>{empresaId}</div>
      <div data-testid='all'>{String(isAllCompanies)}</div>
      <div data-testid='scope'>{scopeCompanyId ?? 'undefined'}</div>
      <div data-testid='canAll'>{String(canSeeAllCompanies)}</div>
    </>
  );
}

function renderWithProvider() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <AppDataProvider>
        <Probe />
      </AppDataProvider>
    </QueryClientProvider>,
  );
}

describe('AppDataProvider — company switcher scope', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.localStorage.clear();
    mockUseCompanies.mockReturnValue({ data: COMPANIES });
  });

  it('a non-admin scoped role only sees the companies granted to it (not others)', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: false, role: 'Requester', companyIds: ['company_ulbra'] } });
    renderWithProvider();
    // Scoped to Ulbra only → CELSP is not selectable, and no "Todas".
    expect(screen.getByTestId('ids')).toHaveTextContent('company_ulbra');
    expect(screen.getByTestId('ids')).not.toHaveTextContent('company_celsp');
    expect(screen.getByTestId('canAll')).toHaveTextContent('false');
    expect(screen.getByTestId('sel')).toHaveTextContent('company_ulbra');
  });

  it('an admin sees every company (bypass)', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: true, companyIds: [] } });
    renderWithProvider();
    expect(screen.getByTestId('ids')).toHaveTextContent('company_ulbra,company_celsp');
  });

  it('Presidency sees every company (scope bypass like Admin), despite no grants', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: false, role: 'Presidency', companyIds: [] } });
    renderWithProvider();
    expect(screen.getByTestId('ids')).toHaveTextContent('company_ulbra,company_celsp');
  });

  it('a consolidated non-see-all role (Finance) also sees every company (AEL-906)', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: false, role: 'Finance', companyIds: ['company_ulbra'] } });
    renderWithProvider();
    expect(screen.getByTestId('ids')).toHaveTextContent('company_ulbra,company_celsp');
    expect(screen.getByTestId('canAll')).toHaveTextContent('true');
  });

  it('a consolidated role defaults to "Todas" on first access (no stored choice)', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: false, role: 'Controllership', companyIds: [] } });
    renderWithProvider();
    expect(screen.getByTestId('sel')).toHaveTextContent(ALL_COMPANIES);
    expect(screen.getByTestId('all')).toHaveTextContent('true');
    // In consolidated mode the scoped companyId sent to the API is omitted.
    expect(screen.getByTestId('scope')).toHaveTextContent('undefined');
  });

  it('a scoped role never gets the "Todas" sentinel — a stale sentinel in storage falls back to a company', () => {
    window.localStorage.setItem('ulbra-one.empresaId', ALL_COMPANIES);
    mockUseMe.mockReturnValue({ data: { isAdmin: false, role: 'Approver', companyIds: ['company_ulbra'] } });
    renderWithProvider();
    expect(screen.getByTestId('all')).toHaveTextContent('false');
    expect(screen.getByTestId('sel')).toHaveTextContent('company_ulbra');
    expect(screen.getByTestId('scope')).toHaveTextContent('company_ulbra');
  });

  it('a consolidated role keeps a manually chosen company (not overridden by the default)', () => {
    window.localStorage.setItem('ulbra-one.empresaId', 'company_celsp');
    mockUseMe.mockReturnValue({ data: { isAdmin: true, companyIds: [] } });
    renderWithProvider();
    expect(screen.getByTestId('sel')).toHaveTextContent('company_celsp');
    expect(screen.getByTestId('all')).toHaveTextContent('false');
  });

  it('keeps a stored real company across the me/companies load race (companies resolve after me)', () => {
    // Reload race: /auth/me resolves before the companies list. Normalization must wait for the
    // list — else it deems the stored company "invalid" (empty list) and clobbers it to the ALL
    // sentinel, which then sticks even after the list loads.
    window.localStorage.setItem('ulbra-one.empresaId', 'company_celsp');
    mockUseMe.mockReturnValue({ data: { isAdmin: true, companyIds: [] } });
    mockUseCompanies.mockReturnValue({ data: undefined }); // list not loaded yet

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const tree = (
      <QueryClientProvider client={qc}>
        <AppDataProvider>
          <Probe />
        </AppDataProvider>
      </QueryClientProvider>
    );
    const { rerender } = render(tree);
    // While the list is still loading, the stored selection must survive (not reset to "Todas").
    expect(screen.getByTestId('sel')).toHaveTextContent('company_celsp');

    // The list resolves — selection stays on the real company.
    mockUseCompanies.mockReturnValue({ data: COMPANIES });
    rerender(tree);
    expect(screen.getByTestId('sel')).toHaveTextContent('company_celsp');
    expect(screen.getByTestId('all')).toHaveTextContent('false');
  });
});
