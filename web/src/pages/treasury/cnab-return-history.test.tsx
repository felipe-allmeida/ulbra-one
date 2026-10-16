import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockHistory, mockDownload } = vi.hoisted(() => ({
  mockHistory: vi.fn(),
  mockDownload: vi.fn(() => Promise.resolve()),
}));

vi.mock('../../queries', () => ({
  useRemittanceReturnHistory: mockHistory,
  useBankAccounts: () => ({ data: [{ id: 'ba_1', alias: 'Banrisul c/c 41.221' }] }),
  useRemittanceReturnPreview: () => ({ mutate: vi.fn(), isPending: false }),
  useRemittanceReturnAutoConfirm: () => ({ mutate: vi.fn(), isPending: false }),
  useConfirmRemittanceLine: () => ({ mutate: vi.fn(), isPending: false }),
  useMarkRemittanceLineReturned: () => ({ mutate: vi.fn(), isPending: false }),
  useMe: () => ({ data: { isAdmin: true, permissions: [] } }),
}));
vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof import('../../permissions')>()),
  usePermissions: () => ({
    me: { isAdmin: true },
    isAdmin: true,
    can: () => true,
    canAny: () => true,
  }),
}));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('../../download', async (orig) => ({
  ...(await orig<typeof import('../../download')>()),
  downloadWithAuth: mockDownload,
}));

import { CnabReturnTab } from './cnab-return-tab';

const renderTab = () =>
  render(
    <MemoryRouter>
      <CnabReturnTab companyId='c1' />
    </MemoryRouter>,
  );

beforeEach(() => {
  mockDownload.mockClear();
  mockHistory.mockReturnValue({
    data: {
      items: [
        {
          id: 'remittance_return_1',
          fileName: 'RETORNO_20260820.RET',
          processedAt: '2026-08-20T13:45:00Z',
          processedBy: 'maria.silva',
          applied: true,
          matched: 12,
          paid: 10,
          returned: 1,
          unmatched: 0,
          divergent: 1,
        },
        {
          id: 'remittance_return_2',
          fileName: 'RETORNO_TRUNCADO.RET',
          processedAt: '2026-08-19T09:00:00Z',
          processedBy: 'joao.souza',
          applied: false,
          matched: 0,
          paid: 0,
          returned: 0,
          unmatched: 3,
          divergent: 0,
        },
      ],
      total: 2,
      totalPages: 1,
    },
  });
});

describe('CnabReturnTab — histórico de retornos', () => {
  it('lists the processed return files for the selected paying account', () => {
    renderTab();

    expect(screen.getByText('RETORNO_20260820.RET')).toBeInTheDocument();
    expect(screen.getByText(/maria\.silva/)).toBeInTheDocument();
  });

  it('flags a file that applied nothing', () => {
    renderTab();

    expect(screen.getByText('Não aplicado')).toBeInTheDocument();
  });

  it('downloads the stored file through the authenticated fetch', () => {
    renderTab();

    fireEvent.click(screen.getAllByRole('button', { name: /Baixar/ })[0]);

    expect(mockDownload).toHaveBeenCalledWith(
      expect.stringContaining('/remittances/return/history/remittance_return_1/file'),
      'RETORNO_20260820.RET',
    );
  });
});
