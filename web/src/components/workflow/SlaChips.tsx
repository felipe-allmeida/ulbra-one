import { useTranslation } from 'react-i18next';
import { Chip } from '../../ui';
import { daysUntil, dueTone } from './sla';

/** Two chips: the current process stage and a due-date urgency chip. `stageLabel` is pre-translated. */
export function SlaChips({ stageLabel, dueDate, now }: { stageLabel: string; dueDate: string; now?: Date }) {
  const { t } = useTranslation();
  const days = daysUntil(dueDate, now);
  const tone = dueTone(days);
  const dueText =
    days === 0
      ? t('workflow.sla.today')
      : days < 0
        ? t('workflow.sla.overdue', { days: -days })
        : t('workflow.sla.dueIn', { days });
  return (
    <span style={{ display: 'inline-flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
      <Chip>{stageLabel}</Chip>
      <span className={`sla-chip sla-${tone}`}>{dueText}</span>
    </span>
  );
}
