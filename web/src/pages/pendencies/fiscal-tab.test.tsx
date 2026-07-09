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
  useUploadPayableFiscalDocument: () => ({ mutate: mockUploadMutate }),
}));
vi.mock('../payables/use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));
vi.mock('../../toast', () => ({ useToast: () => ({ success: mockToastSuccess, error: vi.fn() }) }));
// Stub the fiscal reader so the drawer test doesn't pull in the presigned-upload/parse hooks; the stub
// exposes a button that fires onUploadKey, mimicking a completed blob upload.
vi.mock('../payables/fiscal-xml-reader', () => ({
  FiscalXmlReader: ({
    onUploadKey,
  }: {
    onUploadKey: (key: string, meta: { fileName: string; contentType: string; sizeBytes: number }) => void;
  }) => (
    <button onClick={() => onUploadKey('key_1', { fileName: 'nf.xml', contentType: 'application/xml', sizeBytes: 10 })}>
      upload-nf
    </button>
  ),
}));

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

  it('closes the drawer and toasts on a successful upload', () => {
    mockUploadMutate.mockImplementation((_vars, opts?: { onSuccess?: () => void }) => opts?.onSuccess?.());
    renderTab();
    fireEvent.click(screen.getByText('Anexar NF'));
    expect(screen.getByText('Anexar Nota Fiscal')).toBeInTheDocument();
    fireEvent.click(screen.getByText('upload-nf'));
    expect(mockUploadMutate).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'pay_1', key: 'key_1', fileName: 'nf.xml' }),
      expect.anything(),
    );
    expect(mockToastSuccess).toHaveBeenCalled();
    expect(screen.queryByText('Anexar Nota Fiscal')).not.toBeInTheDocument();
  });
});
