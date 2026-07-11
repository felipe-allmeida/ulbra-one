import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';

const { mockUseApp } = vi.hoisted(() => ({ mockUseApp: vi.fn() }));
vi.mock('../store', () => ({ useApp: mockUseApp }));
vi.mock('./shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('./payables/accounting-tab', () => ({ AccountingTab: () => <div data-testid='accounting-tab' /> }));
vi.mock('./payables/advances-tab', () => ({ AdvancesTab: () => <div data-testid='advances-tab' /> }));

import { ContabilDespesasPage } from './contabil-despesas';

beforeEach(() => {
  mockUseApp.mockReturnValue({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' });
});

function renderPage() {
  return render(
    <MemoryRouter>
      <ContabilDespesasPage />
    </MemoryRouter>,
  );
}

describe('ContabilDespesasPage', () => {
  it('renders the accounting worklist (default tab) when a company is selected', () => {
    renderPage();
    expect(screen.getAllByText('Registro de Despesa').length).toBeGreaterThan(0);
    expect(screen.getByTestId('accounting-tab')).toBeInTheDocument();
    expect(screen.queryByTestId('advances-tab')).not.toBeInTheDocument();
  });

  it('switches to the Adiantamentos tab', () => {
    renderPage();
    fireEvent.click(screen.getByText('Adiantamentos'));
    expect(screen.getByTestId('advances-tab')).toBeInTheDocument();
    expect(screen.queryByTestId('accounting-tab')).not.toBeInTheDocument();
  });

  it('shows an empty state when no company is selected', () => {
    mockUseApp.mockReturnValue({ empresa: null, empresaId: '' });
    renderPage();
    expect(screen.queryByTestId('accounting-tab')).not.toBeInTheDocument();
    expect(screen.getByText('Selecione uma empresa')).toBeInTheDocument();
  });
});
