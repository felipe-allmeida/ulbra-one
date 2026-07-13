/**
 * Tests for the payable intake UI (3-card modality chooser + NF sub-choice + reader + manual):
 * - The chooser renders the 3 modality cards (Folha disabled/"Em breve") and nothing else.
 * - Nota Fiscal opens a 2-block sub-choice: "Enviar arquivo" (opens the file picker directly) vs "Preencher manualmente".
 * - Adiantamento goes straight to the blank form, with no NF reader at all.
 * - The intake modality maps to the origin sent on create (originForIntake + wired submit).
 * - The top back link returns to the chooser (not the list) from inside a modality.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n';

const { navMock } = vi.hoisted(() => ({ navMock: vi.fn() }));

// ---- mock react-router-dom (NewPayablePage calls useNavigate, useParams) ----
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navMock, useParams: () => ({}) };
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
    useCostCenters: vi.fn(() => ({ data: [] })),
    useExpenseCategories: () => ({ data: [] }),
    useLedgerAccounts: () => ({ data: [] }),
    useCreatePayable: vi.fn(() => noopMut()),
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
import { originForIntake } from './payables/intake-choice';
import {
  useRequestFiscalUploadUrl,
  useParseFiscalDocument,
  uploadToPresignedUrl,
  useCreatePayable,
  useCostCenters,
} from '../queries';
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

/** Wires the presigned upload-url → PUT → parse chain (mutateAsync-based) to resolve with the parsed NF. */
function mockNfFlow(parsed: ParsedFiscalDocument) {
  const uploadTarget: UploadTarget = { uploadUrl: 'https://blob.example.com/up?sig=test', key: 'fiscal/nf001.xml' };
  const requestUrlMutateAsync = vi.fn(async (_vars: unknown) => uploadTarget);
  const parseMutateAsync = vi.fn(async (_vars: unknown) => parsed);
  vi.mocked(useRequestFiscalUploadUrl).mockReturnValue({
    mutate: vi.fn(),
    mutateAsync: requestUrlMutateAsync,
    isPending: false,
    error: null,
    reset: vi.fn(),
    isSuccess: false,
    data: undefined,
  } as unknown as ReturnType<typeof useRequestFiscalUploadUrl>);
  vi.mocked(useParseFiscalDocument).mockReturnValue({
    mutate: vi.fn(),
    mutateAsync: parseMutateAsync,
    isPending: false,
    error: null,
    reset: vi.fn(),
    isSuccess: false,
    data: undefined,
  } as unknown as ReturnType<typeof useParseFiscalDocument>);
  vi.mocked(uploadToPresignedUrl).mockResolvedValue(undefined);
  return { uploadTarget, requestUrlMutateAsync, parseMutateAsync };
}

async function uploadNf(user: ReturnType<typeof userEvent.setup>) {
  const inputs = document.querySelectorAll<HTMLInputElement>('input[type="file"]');
  expect(inputs.length).toBeGreaterThan(0);
  const xmlFile = new File(['<nfeProc/>'], 'nf001.xml', { type: 'text/xml' });
  await user.upload(inputs[0], xmlFile);
  return xmlFile;
}

describe('Payable intake (modality chooser + NF reader + manual)', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders the 3 modality cards and nothing else', () => {
    renderPage();

    expect(screen.getByText('Folha de pagamento')).toBeInTheDocument();
    expect(screen.getByText('Nota Fiscal')).toBeInTheDocument();
    expect(screen.getByText('Adiantamento')).toBeInTheDocument();
    // Neither the NF reader nor the form render before a modality is picked.
    expect(screen.queryByText('Nota fiscal (XML/PDF)')).toBeNull();
    expect(screen.queryByText('ou preencher manualmente')).toBeNull();
    expect(screen.queryByText('Centro de custo')).toBeNull();
  });

  it('Folha de pagamento is disabled with an "Em breve" badge', () => {
    renderPage();

    const folha = screen.getByRole('button', { name: /Folha de pagamento/ });
    expect(folha).toBeDisabled();
    expect(screen.getByText('Em breve')).toBeInTheDocument();

    fireEvent.click(folha);
    // Nothing changes: still on the chooser.
    expect(screen.queryByText('Nota fiscal (XML/PDF)')).toBeNull();
    expect(screen.queryByText('Centro de custo')).toBeNull();
  });

  it('Nota Fiscal card shows the 2-block sub-choice (file vs manual)', () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /Nota Fiscal/ }));

    // The fiscal sub-choice: two blocks, not the reader nor the form yet.
    expect(screen.getByText('Como você quer informar a Nota Fiscal?')).toBeInTheDocument();
    expect(screen.getByText('Enviar arquivo')).toBeInTheDocument(); // a file-input label, not a button
    expect(screen.getByRole('button', { name: /Preencher manualmente/ })).toBeInTheDocument();
    expect(screen.queryByText('Dados da Nota Fiscal')).toBeNull();
    expect(screen.queryByText('Centro de custo')).toBeNull();
  });

  it('Nota Fiscal → "Enviar arquivo" reads the picked file and reveals the form directly (no extra screen)', async () => {
    const user = userEvent.setup();
    mockNfFlow(minParsed);
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /Nota Fiscal/ }));
    // The "Enviar arquivo" block is itself the file input — picking a file goes straight to processing.
    await uploadNf(user);

    await waitFor(() => {
      expect(screen.getByText('Centro de custo')).toBeInTheDocument();
    });
    // The reader shows the parsed NF band; no intermediate upload screen was needed.
    expect(screen.getByText('Dados da Nota Fiscal')).toBeInTheDocument();
  });

  it('Nota Fiscal → "Preencher manualmente" reveals the blank form (NF not available yet)', async () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /Nota Fiscal/ }));
    fireEvent.click(screen.getByRole('button', { name: /Preencher manualmente/ }));

    await waitFor(() => {
      expect(screen.getByText('Centro de custo')).toBeInTheDocument();
    });
    // The reader collapses into the band, still available to attach the NF later.
    expect(screen.getByText('Dados da Nota Fiscal')).toBeInTheDocument();
  });

  it('Adiantamento reveals the blank form with no NF reader at all', async () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /Adiantamento/ }));

    await waitFor(() => {
      expect(screen.getByText('Centro de custo')).toBeInTheDocument();
    });
    expect(screen.queryByText('Nota fiscal (XML/PDF)')).toBeNull();
    expect(screen.queryByText('Dados da Nota Fiscal')).toBeNull();
    expect(screen.queryByText('ou preencher manualmente')).toBeNull();
  });

  it('back link from inside a modality returns to the chooser (not the list)', async () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /Adiantamento/ }));
    await waitFor(() => expect(screen.getByText('Centro de custo')).toBeInTheDocument());

    fireEvent.click(screen.getByText('Voltar'));

    await waitFor(() => expect(screen.getByText('Folha de pagamento')).toBeInTheDocument());
    expect(screen.queryByText('Centro de custo')).toBeNull();
    expect(navMock).not.toHaveBeenCalled();
  });

  it('maps the intake modality to the payable origin (originForIntake)', () => {
    expect(originForIntake('advance', false)).toBe('Advance');
    expect(originForIntake('fiscal', true)).toBe('FiscalDocument');
    // Manual sub-path of the fiscal modality (NF not available yet) stays Manual.
    expect(originForIntake('fiscal', false)).toBe('Manual');
  });

  it('NF flow: upload-url → PUT → parse → prefilled form → create sends origin FiscalDocument', async () => {
    const user = userEvent.setup();
    const { uploadTarget, requestUrlMutateAsync, parseMutateAsync } = mockNfFlow({
      ...minParsed,
      matchedVendorId: 'vendor_1',
      matchedVendorName: 'Fornecedor SA',
      serviceDescription: 'Materiais de escritório',
      installments: [{ dueDate: '2026-08-10', amount: 1500 }],
    });

    vi.mocked(useCostCenters).mockReturnValue({
      data: [{ id: 'cc_1', code: 'CC1', name: 'TI' }],
    } as unknown as ReturnType<typeof useCostCenters>);

    const createAsync = vi.fn().mockResolvedValue({ id: 'payable_1' });
    vi.mocked(useCreatePayable).mockReturnValue({
      mutate: vi.fn(),
      mutateAsync: createAsync,
      isPending: false,
      error: null,
      reset: vi.fn(),
      isSuccess: false,
      data: undefined,
    } as unknown as ReturnType<typeof useCreatePayable>);

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /Nota Fiscal/ }));
    // "Enviar arquivo" is the file input itself — picking a file kicks off the presigned chain.
    const xmlFile = await uploadNf(user);

    // Presigned chain: upload-url with the file name → PUT → parse by storage key.
    await waitFor(() => {
      expect(requestUrlMutateAsync).toHaveBeenCalledWith({ fileName: 'nf001.xml' });
      expect(uploadToPresignedUrl).toHaveBeenCalledWith(uploadTarget.uploadUrl, xmlFile);
      expect(parseMutateAsync).toHaveBeenCalledWith({
        companyId: 'company_1',
        key: uploadTarget.key,
        fileName: 'nf001.xml',
      });
    });

    // Form revealed with the NF fields prefilled (NF-001 in the reader preview and the form input).
    await waitFor(() => {
      expect(screen.getAllByDisplayValue('NF-001').length).toBeGreaterThan(0);
    });

    // Complete the one missing required field (cost center) and submit.
    const selects = document.querySelectorAll<HTMLSelectElement>('select');
    const ccSelect = Array.from(selects).find((s) => s.querySelector('option[value="cc_1"]'))!;
    fireEvent.change(ccSelect, { target: { value: 'cc_1' } });

    const submit = await screen.findByRole('button', { name: 'Lançar' });
    await waitFor(() => expect(submit).toBeEnabled());
    fireEvent.click(submit);

    await waitFor(() => {
      expect(createAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          origin: 'FiscalDocument',
          vendorId: 'vendor_1',
          costCenterId: 'cc_1',
          installments: [{ dueDate: '2026-08-10', amount: 1500 }],
        }),
      );
    });
  });
});
