import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n'; // pt-BR labels.

// Gating in-view por cargo nas telas de Contas a pagar. Fonte da verdade é o backend
// (RequirePermission): excluir anexo e classificar são backed por payables:write — as ações
// só aparecem para quem tem a permissão (Admin faz bypass em usePermissions).
const { permState } = vi.hoisted(() => ({ permState: { canWrite: false } }));
vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof import('../../permissions')>()),
  usePermissions: () => ({
    can: (p: string) => (p === 'payables:write' ? permState.canWrite : false),
    canAny: () => permState.canWrite,
    me: null,
    isAdmin: false,
  }),
}));

const { mockStageItems } = vi.hoisted(() => ({ mockStageItems: vi.fn() }));
vi.mock('../../queries', () => ({ usePayableStageItems: mockStageItems }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));
vi.mock('./classification-drawer', () => ({ ClassificationDrawer: () => null }));

import { AttachmentsPanel } from './attachments-panel';
import { ClassificationTab } from './classification-tab';

beforeEach(() => {
  permState.canWrite = false;
});

describe('AttachmentsPanel — gating de excluir anexo (payables:write)', () => {
  const attachment = { id: 'att_1', fileName: 'nota.pdf', contentType: 'application/pdf', sizeBytes: 10 };
  const payable = { id: 'pay_1', attachments: [attachment] } as never;

  it('esconde "Remover" sem payables:write, mas mantém Visualizar/Baixar (leitura)', () => {
    render(<AttachmentsPanel payable={payable} onDelete={vi.fn()} />);
    expect(screen.queryByRole('button', { name: 'Remover' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Visualizar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Baixar' })).toBeInTheDocument();
  });

  it('mostra "Remover" com payables:write', () => {
    permState.canWrite = true;
    render(<AttachmentsPanel payable={payable} onDelete={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Remover' })).toBeInTheDocument();
  });
});

describe('ClassificationTab — gating da ação Classificar (payables:write)', () => {
  const items = [
    {
      payableId: 'pay_1', counterpartyName: 'ACME', description: 'NF 1', costCenterId: 'cc_1',
      categoryId: null, firstDueDate: '2026-07-20', installmentCount: 1, totalAmount: 1000,
      totalWithheld: 0, netTotal: 1000, status: 'PendingClassification', origin: 'Manual', hasFiscalDocument: false,
    },
  ];

  const renderTab = () =>
    render(
      <MemoryRouter>
        <ClassificationTab companyId='company_1' />
      </MemoryRouter>,
    );

  beforeEach(() => {
    mockStageItems.mockReturnValue({ data: { items, total: 1, totalPages: 1 } });
  });

  it('esconde a ação Classificar sem payables:write, mantendo a lista (leitura)', () => {
    renderTab();
    expect(screen.getByText('NF 1')).toBeInTheDocument(); // lista (leitura) segue visível
    expect(screen.queryByRole('button', { name: 'Classificar título' })).not.toBeInTheDocument();
  });

  it('mostra a ação Classificar com payables:write', () => {
    permState.canWrite = true;
    renderTab();
    expect(screen.getByRole('button', { name: 'Classificar título' })).toBeInTheDocument();
  });
});
