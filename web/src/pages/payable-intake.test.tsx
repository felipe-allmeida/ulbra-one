/**
 * Tests for the payable intake UI (NF reader + manual reveal-gate):
 * - The NF reader (hero) and the "fill manually" option render before any choice is made.
 * - Clicking "ou preencher manualmente" reveals the blank form.
 * - Uploading an NF triggers the presigned flow: upload-url → PUT → parse → applyParsed (mocked hooks).
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n';

// ---- mock react-router-dom (NewPayablePage calls useNavigate, useParams) ----
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn(), useParams: () => ({}) };
});

// ---- mock the store (useApp needs empresa / empresaId) ----
vi.mock('../store', async (orig) => {
  const actual = await orig<typeof import('../store')>();
  return {
    ...actual,
    useApp: () => ({
      empresa: { id: 'company_1', legalName: 'ACME LTDA', cnpj: '00000000000191' },
      empresaId: 'company_1',
    }),
  };
});

// ---- mock all the query hooks used by the page ----
// Keep real module shape so unused hooks don't crash.
vi.mock('../queries', async (orig) => {
  const actual = await orig<typeof import('../queries')>();

  const noop = () => ({ data: undefined, isLoading: false, error: null, isPending: false });
  const noopMut = () => ({
    mutate: vi.fn(),
    mutateAsync: vi.fn(),
    isPending: false,
    error: null,
    reset: vi.fn(),
    isSuccess: false,
    data: undefined,
  });

  return {
    ...actual,
    usePayables: () => ({ data: { items: [], total: 0, totalPages: 1 }, isLoading: false }),
    usePayable: noop,
    useVendors: () => ({ data: { items: [] }, isLoading: false }),
    useCostCenters: () => ({ data: [] }),
    useExpenseCategories: () => ({ data: [] }),
    useChartAccounts: () => ({ data: [] }),
    useCreatePayable: noopMut,
    useUpdatePayable: noopMut,
    useUploadPayableAttachment: noopMut,
    useUploadPayableFiscalDocument: noopMut,
    useRequestFiscalUploadUrl: vi.fn(() => noopMut()),
    useParseFiscalDocument: vi.fn(() => noopMut()),
    uploadToPresignedUrl: vi.fn(),
    useApprovePayable: noopMut,
    useRejectPayable: noopMut,
    useCancelPayable: noopMut,
    useRegisterPayment: noopMut,
    useReversePayment: noopMut,
    useUploadPaymentProof: noopMut,
    useBankAccounts: () => ({ data: [] }),
    useDeletePayableAttachment: noopMut,
    useClassificationSuggestion: () => ({ data: undefined }),
  };
});

import { NewPayablePage } from './payables';
import { useRequestFiscalUploadUrl, useParseFiscalDocument, uploadToPresignedUrl } from '../queries';
import type { ParsedFiscalDocument, UploadTarget } from '../api';

const minParsed: ParsedFiscalDocument = {
  format: 'NfeXml',
  rawExtractionJson: '{}',
  installments: [],
  documentNumber: 'NF-001',
  issueDate: '2024-03-01',
  totalAmount: 1500,
  issuerDocument: '11222333000181',
  issuerName: 'FORNECEDOR SA',
  matchedVendorId: null,
  matchedVendorName: null,
};

function renderPage() {
  return renderWithProviders(<NewPayablePage />);
}

describe('Payable intake (NF reader + manual)', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders the NF reader and the manual option before any choice is made', () => {
    renderPage();

    // The fiscal NF reader (hero) and the "fill manually" link are both offered up front.
    expect(screen.getByText('Nota fiscal (XML/PDF)')).toBeInTheDocument();
    expect(screen.getByText('ou preencher manualmente')).toBeInTheDocument();
    // The payable form fields are not visible until a choice is made.
    expect(screen.queryByText('Centro de custo')).toBeNull();
  });

  it('clicking "ou preencher manualmente" reveals the blank form', async () => {
    renderPage();

    fireEvent.click(screen.getByText('ou preencher manualmente'));

    // After clicking, the form should appear (Centro de custo field).
    await waitFor(() => {
      expect(screen.getByText('Centro de custo')).toBeInTheDocument();
    });
  });

  it('NF flow: upload-url → PUT → parse → form fields pre-filled', async () => {
    const user = userEvent.setup();

    const uploadTarget: UploadTarget = {
      uploadUrl: 'https://blob.example.com/up?sig=test',
      key: 'fiscal/nf001.xml',
    };

    // Set up the upload-url mutation to call onSuccess with the target.
    const requestUrlMutate = vi.fn((_vars: unknown, opts?: { onSuccess?: (d: UploadTarget) => void }) => {
      opts?.onSuccess?.(uploadTarget);
    });
    const parseMutate = vi.fn((_vars: unknown, opts?: { onSuccess?: (d: ParsedFiscalDocument) => void }) => {
      opts?.onSuccess?.(minParsed);
    });

    vi.mocked(useRequestFiscalUploadUrl).mockReturnValue({
      mutate: requestUrlMutate,
      mutateAsync: vi.fn(),
      isPending: false,
      error: null,
      reset: vi.fn(),
      isSuccess: false,
      data: undefined,
    } as unknown as ReturnType<typeof useRequestFiscalUploadUrl>);

    vi.mocked(useParseFiscalDocument).mockReturnValue({
      mutate: parseMutate,
      mutateAsync: vi.fn(),
      isPending: false,
      error: null,
      reset: vi.fn(),
      isSuccess: false,
      data: undefined,
    } as unknown as ReturnType<typeof useParseFiscalDocument>);

    vi.mocked(uploadToPresignedUrl).mockResolvedValue(undefined);

    renderPage();

    // The file input is hidden inside the label; query it via DOM.
    const inputs = document.querySelectorAll<HTMLInputElement>('input[type="file"]');
    expect(inputs.length).toBeGreaterThan(0);
    const nfInput = inputs[0];

    const xmlFile = new File(['<nfeProc/>'], 'nf001.xml', { type: 'text/xml' });
    await user.upload(nfInput, xmlFile);

    // upload-url was requested with the file name
    expect(requestUrlMutate).toHaveBeenCalledWith(
      { fileName: 'nf001.xml' },
      expect.objectContaining({ onSuccess: expect.any(Function) }),
    );

    // File was PUT to the presigned URL
    expect(uploadToPresignedUrl).toHaveBeenCalledWith(uploadTarget.uploadUrl, xmlFile);

    // Parse was called with the storage key (not the raw file)
    expect(parseMutate).toHaveBeenCalledWith(
      { companyId: 'company_1', key: uploadTarget.key, fileName: 'nf001.xml' },
      expect.objectContaining({ onSuccess: expect.any(Function) }),
    );

    // After parse succeeds, the form is revealed and the document number pre-filled.
    // NF-001 appears in both the reader's read-only preview and the form input.
    await waitFor(() => {
      expect(screen.getAllByDisplayValue('NF-001').length).toBeGreaterThan(0);
    });

    // The parsed NF has an issuerDocument but no matched vendor, so the
    // prefilled "register vendor" action must be offered.
    expect(screen.getByText('Cadastrar fornecedor')).toBeInTheDocument();
  });
});
