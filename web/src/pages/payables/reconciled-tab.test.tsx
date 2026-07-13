import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageItems } = vi.hoisted(() => ({ mockUseStageItems: vi.fn() }));
vi.mock('../../queries', () => ({ usePayableStageItems: mockUseStageItems }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));

import { ReconciledTab } from './reconciled-tab';

const item = {
  payableId: 'pay_1',
  counterpartyName: 'Fornecedor A',
  description: 'Serviço X',
  costCenterId: 'cc_1',
  categoryId: null,
  firstDueDate: '2026-07-20',
  installmentCount: 1,
  totalAmount: 1000,
  totalWithheld: 60,
  netTotal: 940,
  status: 'Paid',
  origin: 'Manual',
  hasFiscalDocument: true,
};

beforeEach(() => {
  mockUseStageItems.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } });
});

describe('ReconciledTab', () => {
  it('lists Paid payables with gross and net columns (net is locked post-accounting)', () => {
    render(
      <MemoryRouter>
        <ReconciledTab companyId='company_1' />
      </MemoryRouter>,
    );
    expect(mockUseStageItems).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'Paid' }),
      expect.anything(),
    );
    expect(screen.getByText('Valor bruto')).toBeInTheDocument();
    expect(screen.getByText('Valor líquido')).toBeInTheDocument();
    expect(screen.getByText(/R\$\s?1\.000/)).toBeInTheDocument();
    expect(screen.getByText(/R\$\s?940/)).toBeInTheDocument();
  });
});
