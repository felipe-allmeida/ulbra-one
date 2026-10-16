/**
 * PayableStatusBadge — the 7-state set renders the correct pt-BR label.
 *
 * The classification / AI-suggestion pre-fill previously exercised here via the standalone
 * ClassificacaoPage now lives in the unified Contábil home (ReviewPendingTab + ClassificationDrawer);
 * that flow is covered by review-pending-tab.test.tsx.
 */
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';

import { PayableStatusBadge, payableStatusKey } from './badges';
import type { PayableStatus } from '../../api';

describe('PayableStatusBadge — new 7-state set', () => {
  const cases: [PayableStatus, string][] = [
    ['Received', 'Recebida'],
    ['PendingClassification', 'Aguardando classificação'],
    ['PendingApproval', 'Aguardando aprovação'],
    ['Scheduled', 'Agendada'],
    ['Paid', 'Paga'],
    ['Rejected', 'Rejeitada'],
    // Terminal: para quem usa é o mesmo desfecho de uma rejeição (ver payableStatusKey).
    ['Cancelled', 'Rejeitada'],
  ];

  it.each(cases)('status %s renders pt-BR label "%s"', (status, expectedLabel) => {
    render(<PayableStatusBadge s={status} />);
    expect(screen.getByText(expectedLabel)).toBeTruthy();
  });
});

/**
 * `Rejected` é o mesmo status para dois desfechos opostos: vindo de requisição é devolução (a requisição
 * corrigida reabre ESTE título via `Reopen`), avulso é rejeição definitiva — nada reabre, porque o único
 * chamador de `Reopen` é o consumer da requisição.
 */
describe('payableStatusKey', () => {
  it('título vindo de requisição: Rejected é devolução', () => {
    expect(payableStatusKey('Rejected', true)).toBe('payables.status.RejectedReturned');
  });

  it('título avulso: Rejected é rejeição definitiva', () => {
    expect(payableStatusKey('Rejected', false)).toBe('payables.status.Rejected');
    expect(payableStatusKey('Rejected')).toBe('payables.status.Rejected');
  });

  it('a distinção não vaza para os outros status', () => {
    expect(payableStatusKey('Cancelled', true)).toBe('payables.status.Cancelled');
    expect(payableStatusKey('Paid', true)).toBe('payables.status.Paid');
  });
});
