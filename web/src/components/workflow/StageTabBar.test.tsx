import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { StageTabBar, type StageTab } from './StageTabBar';

const tabs: StageTab[] = [
  { key: 'entry', label: 'Entrada', count: 5 },
  { key: 'approval', label: 'Aprovação', count: 2, badge: 'danger' },
  { key: 'paid', label: 'Conciliado' },
];

describe('StageTabBar', () => {
  it('renders all tab labels and counts', () => {
    render(<StageTabBar tabs={tabs} active='entry' onChange={() => {}} />);
    expect(screen.getByText('Entrada')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
    expect(screen.getByText('Aprovação')).toBeInTheDocument();
  });
  it('marks the active tab with aria-selected', () => {
    render(<StageTabBar tabs={tabs} active='approval' onChange={() => {}} />);
    expect(screen.getByRole('tab', { name: /Aprovação/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: /Entrada/ })).toHaveAttribute('aria-selected', 'false');
  });
  it('calls onChange with the tab key when clicked', () => {
    const onChange = vi.fn();
    render(<StageTabBar tabs={tabs} active='entry' onChange={onChange} />);
    fireEvent.click(screen.getByRole('tab', { name: /Conciliado/ }));
    expect(onChange).toHaveBeenCalledWith('paid');
  });
});
