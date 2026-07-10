import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageItems, mockUploadMutate, mockToastSuccess } = vi.hoisted(() => ({
  mockUseStageItems: vi.fn(),
  mockUploadMutate: vi.fn(),
  mockToastSuccess: vi.fn(),
}));
vi.mock('../../queries', () => ({
  usePayableStageItems: mockUseStageItems,
  useUploadPayableAttachment: () => ({ mutate: mockUploadMutate, isPending: false }),
}));
vi.mock('../payables/use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));
vi.mock('../../toast', () => ({ useToast: () => ({ success: mockToastSuccess, error: vi.fn() }) }));
// Stub only the FilePicker (keep the real Drawer/Panel/Button/Field); its button fires onSelect with a fake file.
vi.mock('../../ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../ui')>();
  return {
    ...actual,
    FilePicker: ({ onSelect }: { onSelect: (f: File | null) => void }) => (
      <button onClick={() => onSelect(new File(['x'], 'nf.pdf', { type: 'application/pdf' }))}>pick-file</button>
    ),
  };
});

import { FiscalTab } from './fiscal-tab';

const item = {
  payableId: 'pay_1',
  counterpartyName: 'Fornecedor A',
  description: 'Serviço X',
  costCenterId: 'cc_1',
  categoryId: null,
  firstDueDate: '2026-07-20',
  installmentCount: 1,
  totalAmount: 1000,
  status: 'PendingAccounting',
  hasFiscalDocument: false,
};

beforeEach(() => {
  mockUseStageItems.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } });
  mockUploadMutate.mockReset();
  mockToastSuccess.mockReset();
});

function renderTab() {
  return render(
    <MemoryRouter>
      <FiscalTab companyId='company_1' />
    </MemoryRouter>,
  );
}

describe('FiscalTab', () => {
  it('lists the payable and queries stage-items with hasFiscalDocument=false', () => {
    renderTab();
    expect(screen.getByText('Serviço X')).toBeInTheDocument();
    expect(screen.getByText('Anexar NF')).toBeInTheDocument();
    expect(mockUseStageItems).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'PendingAccounting', hasFiscalDocument: false }),
      expect.anything(),
    );
  });

  it('opens the attach drawer when "Anexar NF" is clicked', () => {
    renderTab();
    fireEvent.click(screen.getByText('Anexar NF'));
    expect(screen.getByText('Anexar Nota Fiscal')).toBeInTheDocument();
  });

  it('uploads the picked file as an attachment and closes the drawer on success', () => {
    mockUploadMutate.mockImplementation((_vars, opts?: { onSuccess?: () => void }) => opts?.onSuccess?.());
    renderTab();
    fireEvent.click(screen.getByText('Anexar NF')); // open drawer
    fireEvent.click(screen.getByText('pick-file')); // select a file (stubbed FilePicker)
    fireEvent.click(screen.getByText('Anexar')); // confirm (footer)
    expect(mockUploadMutate).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'pay_1', file: expect.any(File) }),
      expect.anything(),
    );
    expect(mockToastSuccess).toHaveBeenCalled();
    expect(screen.queryByText('Anexar Nota Fiscal')).not.toBeInTheDocument();
  });
});
