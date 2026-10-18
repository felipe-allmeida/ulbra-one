import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n'; // pt-BR labels.

/**
 * Regression (AEL-865): the "A caminho do Financeiro" panel must NOT decompose the scheduled finance
 * queue into "prontos vs aguardando NF" — payment (Scheduled) and fiscalDocumentPending
 * (PendingAccounting|Scheduled|Paid without NF) are different populations, so the old
 * `payment.count − fiscalDocumentPending.count` was misleading. It now shows the scheduled count
 * directly; "Aguardando NF" is a standalone KPI.
 */
vi.mock('../../queries', () => ({
  usePayableStageSummary: () => ({
    data: {
      payment: { count: 3, totalAmount: 44700, netTotalAmount: 44700 },
      fiscalDocumentPending: { count: 5, totalAmount: 0, netTotalAmount: null },
    },
  }),
  usePayableStageItems: () => ({ data: { total: 7 } }),
}));

import { ContabilAside } from './contabil-aside';

describe('ContabilAside — A caminho do Financeiro (AEL-865)', () => {
  it('shows the scheduled-queue count directly, never a cross-population subtraction', () => {
    renderWithProviders(<ContabilAside companyId='c1' />);
    // "Títulos agendados" = payment.count (3), NOT max(0, 3 - 5) = 0 from the old buggy math.
    const scheduledRow = screen.getByText('Títulos agendados').closest('.cfrow');
    expect(scheduledRow).toBeTruthy();
    expect(scheduledRow?.textContent).toContain('3');
    // The invalid "Prontos p/ pagar" decomposition is gone.
    expect(screen.queryByText('Prontos p/ pagar')).toBeNull();
  });

  it('surfaces the standalone KPIs (A classificar, Aguardando NF)', () => {
    renderWithProviders(<ContabilAside companyId='c1' />);
    // "Precisa de você": A classificar = reviewPending total (7).
    const toClassify = screen.getByText('A classificar').closest('.ktile');
    expect(toClassify?.textContent).toContain('7');
    // Aguardando NF = fiscalDocumentPending.count (5), as a standalone figure.
    const awaitingNf = screen.getByText('Aguardando NF').closest('.ktile');
    expect(awaitingNf?.textContent).toContain('5');
  });

  it('notes the NF-pending titles factually (no "antes de seguir" blockage claim)', () => {
    renderWithProviders(<ContabilAside companyId='c1' />);
    expect(screen.getByText('5 títulos ainda sem nota fiscal anexada.')).toBeTruthy();
  });
});
