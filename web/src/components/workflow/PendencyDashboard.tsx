import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { brl } from '../../lib';

export interface PendencyCard {
  stageKey: string;
  label: string;
  icon: ReactNode;
  count: number;
  /** Aggregate amount for the stage. Omitted when the domain has no per-stage amount (e.g. requisitions). */
  totalAmount?: number;
}

/**
 * Horizontal row of pending-per-stage cards; each card jumps to its stage tab. `label` is pre-translated.
 * `itemsCountKey`/`viewLabelKey` let each domain swap the vocabulary (e.g. "requisições" instead of
 * "títulos"); the amount only shows when the card provides `totalAmount`.
 */
export function PendencyDashboard({
  cards,
  onCardClick,
  itemsCountKey = 'workflow.dashboard.itemsCount',
  viewLabelKey = 'workflow.dashboard.viewPending',
}: {
  cards: PendencyCard[];
  onCardClick: (stageKey: string) => void;
  itemsCountKey?: string;
  viewLabelKey?: string;
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
          <div className='pendency-card-count'>{t(itemsCountKey, { count: card.count })}</div>
          {card.totalAmount != null && <div className='pendency-card-amount'>{brl(card.totalAmount)}</div>}
          <span className='pendency-card-link'>{t(viewLabelKey)}</span>
        </button>
      ))}
    </div>
  );
}
