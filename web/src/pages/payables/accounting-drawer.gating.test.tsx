import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n'; // pt-BR labels.

// In-view gating of the "Registro de Despesa" drawer. Contabilizar / Reclassificar /
// retenções / anexar NF são backed por payables:write no backend — o drawer só mostra
// essas ações para quem tem a permissão (Admin faz bypass em usePermissions).
const { permState } = vi.hoisted(() => ({ permState: { canWrite: false } }));

vi.mock('../../queries', () => {
  const noop = () => ({ mutate: vi.fn(), isPending: false });
  return {
  usePayable: () => ({
    data: { id: 'pay_1', counterpartyName: 'ACME', accountingAccountId: null, attachments: [] },
  }),
  usePayableAccountingView: () => ({
    data: { hasFiscalDocument: true, origin: 'Manual', counterpartyName: 'ACME' },
    refetch: vi.fn(),
  }),
  useConfirmAccounting: noop,
  useLedgerAccounts: () => ({ data: [] }),
  useClassifyPayable: noop,
  useUploadPayableAttachment: noop,
  };
});
vi.mock('../../permissions', async (orig) => ({
  ...(await orig<typeof import('../../permissions')>()),
  usePermissions: () => ({ can: () => permState.canWrite, canAny: () => permState.canWrite, me: null, isAdmin: false }),
}));
vi.mock('../../toast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('./withholding-section', () => ({
  WithholdingSection: ({ editable }: { editable: boolean }) => (
    <div data-testid='withholding' data-editable={String(editable)} />
  ),
}));

import { AccountingDrawer } from './accounting-drawer';

const renderDrawer = () =>
  render(
    <MemoryRouter>
      <AccountingDrawer payableId='pay_1' onClose={vi.fn()} />
    </MemoryRouter>,
  );

beforeEach(() => {
  permState.canWrite = false;
});

describe('AccountingDrawer — gating de ações (payables:write)', () => {
  it('esconde Contabilizar / Reclassificar / retenções editáveis sem payables:write', () => {
    renderDrawer();
    expect(screen.queryByRole('button', { name: 'Contabilizar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reclassificar' })).not.toBeInTheDocument();
    expect(screen.getByTestId('withholding').getAttribute('data-editable')).toBe('false');
  });

  it('mostra as ações e retenções editáveis com payables:write', () => {
    permState.canWrite = true;
    renderDrawer();
    expect(screen.getByRole('button', { name: 'Contabilizar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reclassificar' })).toBeInTheDocument();
    expect(screen.getByTestId('withholding').getAttribute('data-editable')).toBe('true');
  });
});
