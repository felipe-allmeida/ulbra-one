import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

const { mockUsePayable, mockClassify } = vi.hoisted(() => ({ mockUsePayable: vi.fn(), mockClassify: vi.fn() }));
vi.mock('../../queries', () => {
  const noop = () => ({ mutate: vi.fn(), isPending: false });
  return {
    usePayable: mockUsePayable,
    usePayableAccountingView: () => ({
      data: { vendor: { name: 'ACME Ltda', document: '12345678000190', taxRegime: 'LucroPresumido' } },
      refetch: vi.fn(),
    }),
    useClassifyPayable: () => ({ mutate: mockClassify, isPending: false }),
    useLedgerAccounts: () => ({ data: [{ id: 'ledgacct_1', code: '3.1', name: 'Despesas' }] }),
    useRequisition: () => ({ data: undefined }),
    useSetPayableWithholdings: noop,
    // "Solicitar NF" now lives in this drawer's footer (it moved off the list row).
    useRequestFiscalDocument: noop,
    useSetPayableDocumentNumber: noop,
  };
});
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));
vi.mock('./installments-table', () => ({ InstallmentsTable: () => <div data-testid='installments' /> }));
// The drawer now reads payables:write (for the editable document-number field); keep it read-only here so these
// tests focus on the review context. usePermissions internally calls useMe/useEffectiveMe (from the fully-mocked
// ../../queries), so stub it directly. Toast stays real (renderWithProviders supplies ToastProvider).
vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof import('../../permissions')>()),
  usePermissions: () => ({ can: () => false, canAny: () => false, me: null, isAdmin: false }),
}));

import { ClassificationDrawer } from './classification-drawer';

beforeEach(() => {
  mockUsePayable.mockReturnValue({
    data: {
      id: 'payable_1',
      companyId: 'company_1',
      counterpartyName: 'ACME',
      costCenterId: 'cc_1',
      documentNumber: null,
      issueDate: null,
      description: 'Serviço de limpeza',
      status: 'Scheduled',
      totalAmount: 1000,
      totalWithheld: 0,
      netTotal: 1000,
      installments: [],
      attachments: [],
      withholdings: [],
      originRequisitionId: null,
      suggestedAccountingAccountId: 'ledgacct_1',
      classificationRationale: 'Fornecedor recorrente de TI',
      accountingAccountId: null,
    },
  });
});

const render = () =>
  renderWithProviders(
    <MemoryRouter>
      <ClassificationDrawer payableId='payable_1' onClose={() => {}} />
    </MemoryRouter>,
  );

describe('ClassificationDrawer', () => {
  it('pre-selects the AI-suggested account and shows the rationale', () => {
    render();
    expect(screen.getByText('Despesas')).toBeInTheDocument();
    expect(screen.getByText(/Fornecedor recorrente/)).toBeInTheDocument();
  });

  it('surfaces the vendor + título review context so the accountant can validate before classifying', () => {
    render();
    // Vendor card (same data as the Registro de Despesa drawer).
    expect(screen.getByDisplayValue('ACME Ltda')).toBeInTheDocument();
    // Título + documentos sections.
    expect(screen.getByText('Dados do título')).toBeInTheDocument();
    expect(screen.getByText('Documentos anexados')).toBeInTheDocument();
  });

  it('AEL-885: "Ver requisição" stamps the current screen so the requisition back returns here', () => {
    // Reproduce the reported flow: Contábil → Classificar (drawer open over the classification stage) →
    // Ver requisição. Clicking it must record where we came from so the requisition's back goes back here.
    mockUsePayable.mockReturnValue({
      data: {
        id: 'payable_1',
        companyId: 'company_1',
        counterpartyName: 'ACME',
        costCenterId: 'cc_1',
        documentNumber: null,
        issueDate: null,
        description: 'Serviço de limpeza',
        status: 'Scheduled',
        totalAmount: 1000,
        totalWithheld: 0,
        netTotal: 1000,
        installments: [],
        attachments: [],
        withholdings: [],
        originRequisitionId: 'req_1',
        suggestedAccountingAccountId: 'ledgacct_1',
        classificationRationale: 'Fornecedor recorrente de TI',
        accountingAccountId: null,
      },
    });
    const Probe = () => {
      const from = (useLocation().state as { from?: string } | null)?.from ?? 'none';
      return <div data-testid='from'>{from}</div>;
    };
    renderWithProviders(
      <MemoryRouter initialEntries={['/app/payables?stage=classification']}>
        <Routes>
          <Route path='/app/payables' element={<ClassificationDrawer payableId='payable_1' onClose={() => {}} />} />
          <Route path='/app/requisitions/:id' element={<Probe />} />
        </Routes>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Ver requisição de origem'));
    expect(screen.getByTestId('from')).toHaveTextContent('/app/payables?stage=classification');
  });

  it('AEL-885: "Ver fornecedor" opens the specific vendor (not the list) and stamps the origin', () => {
    mockUsePayable.mockReturnValue({
      data: {
        id: 'payable_1',
        companyId: 'company_1',
        counterpartyName: 'ACME',
        costCenterId: 'cc_1',
        documentNumber: null,
        issueDate: null,
        description: 'Serviço de limpeza',
        status: 'Scheduled',
        totalAmount: 1000,
        totalWithheld: 0,
        netTotal: 1000,
        installments: [],
        attachments: [],
        withholdings: [],
        vendorId: 'vendor_42',
        originRequisitionId: null,
        suggestedAccountingAccountId: 'ledgacct_1',
        classificationRationale: 'Fornecedor recorrente de TI',
        accountingAccountId: null,
      },
    });
    const Probe = () => {
      const loc = useLocation();
      const from = (loc.state as { from?: string } | null)?.from ?? 'none';
      return <div data-testid='vendor-route'>{`${loc.pathname}|${from}`}</div>;
    };
    renderWithProviders(
      <MemoryRouter initialEntries={['/app/payables?stage=accounting']}>
        <Routes>
          <Route path='/app/payables' element={<ClassificationDrawer payableId='payable_1' onClose={() => {}} />} />
          <Route path='/app/vendors/:id' element={<Probe />} />
        </Routes>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Ver fornecedor'));
    // Lands on the specific vendor detail, carrying the accounting screen as back-origin.
    expect(screen.getByTestId('vendor-route')).toHaveTextContent(
      '/app/vendors/vendor_42|/app/payables?stage=accounting',
    );
  });
});
