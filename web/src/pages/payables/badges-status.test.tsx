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

import { PayableStatusBadge } from './badges';
import type { PayableStatus } from '../../api';

describe('PayableStatusBadge — new 7-state set', () => {
  const cases: [PayableStatus, string][] = [
    ['Received', 'Recebida'],
    ['PendingClassification', 'Aguardando classificação'],
    ['PendingApproval', 'Aguardando aprovação'],
    ['Scheduled', 'Agendada'],
    ['Paid', 'Paga'],
    ['Rejected', 'Rejeitada'],
    ['Cancelled', 'Cancelada'],
  ];

  it.each(cases)('status %s renders pt-BR label "%s"', (status, expectedLabel) => {
    render(<PayableStatusBadge s={status} />);
    expect(screen.getByText(expectedLabel)).toBeTruthy();
  });
});
