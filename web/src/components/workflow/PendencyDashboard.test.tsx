import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import '../../i18n';
import { PendencyDashboard, type PendencyCard } from './PendencyDashboard';

const cards: PendencyCard[] = [
  { stageKey: 'classification', label: 'Classificação', icon: <span data-testid='ic1' />, count: 3, totalAmount: 8750 },
  { stageKey: 'approval', label: 'Aprovação', icon: <span data-testid='ic2' />, count: 2, totalAmount: 45000 },
];

describe('PendencyDashboard', () => {
  it('renders a card per stage with count and formatted amount', () => {
    render(<PendencyDashboard cards={cards} onCardClick={() => {}} />);
    expect(screen.getByText('Classificação')).toBeInTheDocument();
    expect(screen.getByText('3 títulos')).toBeInTheDocument();
    expect(screen.getByText('R$ 8.750,00')).toBeInTheDocument();
  });
  it('calls onCardClick with the stage key when a card is clicked', () => {
    const onCardClick = vi.fn();
    render(<PendencyDashboard cards={cards} onCardClick={onCardClick} />);
    fireEvent.click(screen.getByText('Aprovação'));
    expect(onCardClick).toHaveBeenCalledWith('approval');
  });
  it('uses the singular form when count is 1', () => {
    const one: PendencyCard[] = [
      { stageKey: 'approval', label: 'Aprovação', icon: <span />, count: 1, totalAmount: 1000 },
    ];
    render(<PendencyDashboard cards={one} onCardClick={() => {}} />);
    expect(screen.getByText('1 título')).toBeInTheDocument();
  });
});
