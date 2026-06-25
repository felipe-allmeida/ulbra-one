import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { brl } from '../../lib';

export interface PendencyCard {
  stageKey: string;
  label: string;
  icon: ReactNode;
  count: number;
  totalAmount: number;
}

/** Horizontal row of pending-per-stage cards; each card jumps to its stage tab. `label` is pre-translated. */
export function PendencyDashboard({
  cards,
  onCardClick,
}: {
  cards: PendencyCard[];
  onCardClick: (stageKey: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <div className='pendency-dashboard'>
      {cards.map((card) => (
        <button key={card.stageKey} type='button' className='pendency-card' onClick={() => onCardClick(card.stageKey)}>
          <div className='pendency-card-head'>
            <span className='pendency-card-icon'>{card.icon}</span>
            <span className='pendency-card-label'>{card.label}</span>
          </div>
          <div className='pendency-card-count'>{t('workflow.dashboard.itemsCount', { count: card.count })}</div>
          <div className='pendency-card-amount'>{brl(card.totalAmount)}</div>
          <span className='pendency-card-link'>{t('workflow.dashboard.viewPending')}</span>
        </button>
      ))}
    </div>
  );
}
