import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';
import { daysUntil, dueTone } from './sla';
import { SlaChips } from './SlaChips';

describe('daysUntil', () => {
  const now = new Date(2026, 5, 25); // 2026-06-25 local
  it('returns positive days for a future date', () => {
    expect(daysUntil('2026-06-30', now)).toBe(5);
  });
  it('returns 0 for today', () => {
    expect(daysUntil('2026-06-25', now)).toBe(0);
  });
  it('returns negative days for an overdue date', () => {
    expect(daysUntil('2026-06-20', now)).toBe(-5);
  });
});

describe('dueTone', () => {
  it('ok when more than 7 days out', () => expect(dueTone(8)).toBe('ok'));
  it('warn between 3 and 7 days', () => expect(dueTone(5)).toBe('warn'));
  it('danger under 3 days', () => expect(dueTone(2)).toBe('danger'));
  it('danger when overdue', () => expect(dueTone(-1)).toBe('danger'));
  it('warn at exactly 7 days (>7 is the ok threshold)', () => expect(dueTone(7)).toBe('warn'));
  it('warn at exactly 3 days', () => expect(dueTone(3)).toBe('warn'));
});

describe('SlaChips', () => {
  const now = new Date(2026, 5, 25);
  it('renders the stage label and a due-in chip', () => {
    render(<SlaChips stageLabel='Aprovação' dueDate='2026-06-30' now={now} />);
    expect(screen.getByText('Aprovação')).toBeInTheDocument();
    expect(screen.getByText('Vence em 5d')).toBeInTheDocument();
  });
  it('renders an overdue chip when past due', () => {
    render(<SlaChips stageLabel='Pagamento' dueDate='2026-06-20' now={now} />);
    expect(screen.getByText('Vencido há 5d')).toBeInTheDocument();
  });
  it('renders a today chip when due today', () => {
    render(<SlaChips stageLabel='Pagamento' dueDate='2026-06-25' now={now} />);
    expect(screen.getByText('Vence hoje')).toBeInTheDocument();
  });
});
