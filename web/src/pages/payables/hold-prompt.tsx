import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Input } from '../../ui';

/** Inline "reason required" prompt for holding an installment (shared by payment tab and treasury queue). */
export function HoldPrompt({ onConfirm, onCancel }: { onConfirm: (reason: string) => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');
  return (
    <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
      <Input
        placeholder={t('workflow.payment.holdReasonPrompt')}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        style={{ flex: '1 1 180px' }}
      />
      <Button
        onClick={() => {
          if (reason.trim()) onConfirm(reason.trim());
        }}
        disabled={!reason.trim()}
      >
        {t('workflow.payment.confirm')}
      </Button>
      <Button variant='ghost' onClick={onCancel}>
        {t('workflow.payment.cancel')}
      </Button>
    </div>
  );
}
