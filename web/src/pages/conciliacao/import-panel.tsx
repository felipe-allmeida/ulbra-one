import { useTranslation } from 'react-i18next';
import { FilePicker, Panel } from '../../ui';

/**
 * "Importar extrato OFX" panel: the standard file picker plus, right below it, a non-blocking
 * warning banner when the imported statement looks like it belongs to a different account.
 */
export function ImportPanel({
  hasBankAccount,
  importing,
  warning,
  onSelect,
}: {
  hasBankAccount: boolean;
  importing: boolean;
  warning: string | null;
  onSelect: (file: File | null) => void;
}) {
  const { t } = useTranslation();
  return (
    <Panel title={t('conciliacao.upload.label')}>
      <FilePicker
        accept='.ofx'
        placeholder={hasBankAccount ? t('conciliacao.upload.help') : t('conciliacao.upload.needBankAccount')}
        onSelect={onSelect}
        large
      />
      {importing && (
        <p className='help' style={{ marginTop: 10 }}>
          {t('conciliacao.upload.importing')}
        </p>
      )}
      {warning && (
        <div className='form-notice warn' style={{ marginTop: 12, marginBottom: 0 }}>
          {warning}
        </div>
      )}
    </Panel>
  );
}
