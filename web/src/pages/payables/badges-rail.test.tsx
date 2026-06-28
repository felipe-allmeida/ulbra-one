import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';
import type { RailLine } from './payment-rail';

vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn(), useParams: () => ({}) };
});

// Import after mocks
import { InstallmentRailChip } from './badges';

const base: RailLine = {
  installmentStatus: 'Open',
  isHeld: false,
  rail: 'Remittance',
  manualReason: null,
  scheduledPaymentDate: '2026-07-15',
  remittanceSequence: null,
};
const today = '2026-07-10';

describe('InstallmentRailChip', () => {
  it('manual → renders "Pagar manual" label', () => {
    renderWithProviders(
      <InstallmentRailChip line={{ ...base, rail: 'Manual', manualReason: 'VendorBankInfoMissing' }} today={today} />,
    );
    expect(screen.getByText('Pagar manual')).toBeTruthy();
  });

  it('manual → title attr shows translated reason', () => {
    renderWithProviders(
      <InstallmentRailChip line={{ ...base, rail: 'Manual', manualReason: 'VendorBankInfoMissing' }} today={today} />,
    );
    const el = screen.getByTitle('Fornecedor sem dados bancários');
    expect(el).toBeTruthy();
  });

  it('held → renders "Na próxima remessa" label', () => {
    renderWithProviders(<InstallmentRailChip line={{ ...base, isHeld: true }} today={today} />);
    expect(screen.getByText('Na próxima remessa')).toBeTruthy();
  });

  it('scheduled future → renders "Agendada" label with date', () => {
    renderWithProviders(<InstallmentRailChip line={base} today={today} />);
    // The chip should include the date somehow
    expect(screen.getByText(/Agendada/)).toBeTruthy();
  });

  it('nextSweep (today) → renders "Na próxima remessa"', () => {
    renderWithProviders(<InstallmentRailChip line={{ ...base, scheduledPaymentDate: '2026-07-10' }} today={today} />);
    expect(screen.getByText('Na próxima remessa')).toBeTruthy();
  });

  it('inRemittance → renders "Remessa #7"', () => {
    renderWithProviders(
      <InstallmentRailChip
        line={{ ...base, installmentStatus: 'InRemittance', remittanceSequence: 7 }}
        today={today}
      />,
    );
    expect(screen.getByText('Remessa #7')).toBeTruthy();
  });

  it('paid → renders "Paga" label (installments.status.Paid)', () => {
    renderWithProviders(<InstallmentRailChip line={{ ...base, installmentStatus: 'Paid' }} today={today} />);
    expect(screen.getByText('Paga')).toBeTruthy();
  });
});
