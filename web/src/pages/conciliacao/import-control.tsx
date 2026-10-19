import { useTranslation } from 'react-i18next';
import { FilePicker, ToolbarField } from '../../ui';

/** Compact OFX import control for the page header, beside the bank-account picker. */
export function ImportControl({
  hasBankAccount,
  importing,
  onSelect,
}: {
  hasBankAccount: boolean;
  importing: boolean;
  onSelect: (file: File | null) => void;
}) {
  const { t } = useTranslation();
  return (
    <ToolbarField label={t('conciliacao.upload.label')}>
      <div style={{ minWidth: 210 }}>
        <FilePicker
          accept='.ofx'
          placeholder={hasBankAccount ? t('conciliacao.upload.button') : t('conciliacao.upload.needBankAccount')}
          onSelect={onSelect}
        />
        {importing && (
          <span className='help' style={{ display: 'block', marginTop: 4 }}>
            {t('conciliacao.upload.importing')}
          </span>
        )}
      </div>
    </ToolbarField>
  );
}
