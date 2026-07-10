import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';

const { mockUseApp } = vi.hoisted(() => ({ mockUseApp: vi.fn() }));
vi.mock('../store', () => ({ useApp: mockUseApp }));
vi.mock('./shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('./payables/accounting-tab', () => ({ AccountingTab: () => <div data-testid='accounting-tab' /> }));

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
  it('renders the accounting worklist when a company is selected', () => {
    renderPage();
    expect(screen.getByText('Registro de Despesa')).toBeInTheDocument();
    expect(screen.getByTestId('accounting-tab')).toBeInTheDocument();
  });

  it('shows an empty state when no company is selected', () => {
    mockUseApp.mockReturnValue({ empresa: null, empresaId: '' });
    renderPage();
    expect(screen.queryByTestId('accounting-tab')).not.toBeInTheDocument();
    expect(screen.getByText('Selecione uma empresa')).toBeInTheDocument();
  });
});
