import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';

const { mockUseApp } = vi.hoisted(() => ({ mockUseApp: vi.fn() }));
vi.mock('../store', () => ({ useApp: mockUseApp }));
// Tab/aside counts — the home only reads the totals; the real data lives in the tab components.
vi.mock('../queries', () => ({
  usePayableStageSummary: () => ({ data: undefined }),
  usePayableStageItems: () => ({ data: undefined }),
}));
vi.mock('./payables/review-pending-tab', () => ({ ReviewPendingTab: () => <div data-testid='review-pending-tab' /> }));
vi.mock('./payables/advances-tab', () => ({ AdvancesTab: () => <div data-testid='advances-tab' /> }));
vi.mock('./payables/contabil-aside', () => ({ ContabilAside: () => <div data-testid='contabil-aside' /> }));

import { ContabilRevisaoPage } from './contabil-revisao';

beforeEach(() => {
  mockUseApp.mockReturnValue({ empresaId: 'company_1', isAllCompanies: false, scopeReady: true });
});

function renderPage() {
  return render(
    <MemoryRouter>
      <ContabilRevisaoPage />
    </MemoryRouter>,
  );
}

describe('ContabilRevisaoPage (home unificada do Contábil)', () => {
  it('shows the review-classification queue as the default tab', () => {
    renderPage();
    expect(screen.getAllByText('Revisão de Contabilidade').length).toBeGreaterThan(0);
    expect(screen.getByTestId('review-pending-tab')).toBeInTheDocument();
    expect(screen.queryByTestId('advances-tab')).not.toBeInTheDocument();
  });

  it('renders the "A caminho do Financeiro" aside when a company is selected', () => {
    renderPage();
    expect(screen.getByTestId('contabil-aside')).toBeInTheDocument();
  });

  // The "Despesas" tab moved out to the Tributação role/page — Contábil now keeps only the Revisão + Adiantamentos tabs.
  it('switches to the Adiantamentos tab', () => {
    renderPage();
    fireEvent.click(screen.getByText('Adiantamentos'));
    expect(screen.getByTestId('advances-tab')).toBeInTheDocument();
    expect(screen.queryByTestId('review-pending-tab')).not.toBeInTheDocument();
  });

  // AEL-906: consolidated "Todas" shows the demands (tabs + aside) instead of the pick-a-company gate.
  it('shows the demands in the consolidated "Todas" view', () => {
    mockUseApp.mockReturnValue({ empresaId: '__all__', isAllCompanies: true, scopeReady: true });
    renderPage();
    expect(screen.getByTestId('review-pending-tab')).toBeInTheDocument();
    expect(screen.getByTestId('contabil-aside')).toBeInTheDocument();
    expect(screen.queryByText('Selecione uma empresa')).not.toBeInTheDocument();
  });

  it('shows an empty state and no aside when no company is selected', () => {
    mockUseApp.mockReturnValue({ empresaId: '', isAllCompanies: false, scopeReady: false });
    renderPage();
    expect(screen.queryByTestId('review-pending-tab')).not.toBeInTheDocument();
    expect(screen.getByText('Selecione uma empresa')).toBeInTheDocument();
    expect(screen.queryByTestId('contabil-aside')).not.toBeInTheDocument();
  });
});
