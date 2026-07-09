import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { type PayableStageItem } from '../../api';
import { useUploadPayableFiscalDocument } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Drawer, Panel } from '../../ui';
import { brl } from '../../lib';
import { fmtDate } from '../../datetime';
import { FiscalXmlReader } from '../payables/fiscal-xml-reader';

/**
 * Attaches a Nota Fiscal to a payable stuck in PendingAccounting without one. Reuses the fiscal reader and
 * persists on upload; on success the drawer closes and the invalidation drops the row + decrements the count.
 * No "Contabilizar" here — completing accounting stays in the Contabilidade tab of /app/payables.
 */
export function AttachNfDrawer({
  companyId,
  item,
  onClose,
}: {
  companyId: string;
  item: PayableStageItem;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const uploadDoc = useUploadPayableFiscalDocument();
  const [file, setFile] = useState<File | null>(null);

  return (
    <Drawer open onClose={onClose} title={t('pendencies.fiscal.drawerTitle')} sub={item.counterpartyName}>
      <div className='info-grid'>
        <InfoField label={t('payables.columns.description')} value={item.description} />
        <InfoField label={t('payables.columns.amount')} value={brl(item.totalAmount)} />
        <InfoField label={t('payables.columns.dueDate')} value={fmtDate(item.firstDueDate)} />
        <InfoField label={t('payables.columns.installments')} value={String(item.installmentCount)} />
      </div>

      <Panel title={t('payables.accounting.fiscalTitle')}>
        <FiscalXmlReader
          companyId={companyId}
          file={file}
          onFile={setFile}
          onApply={() => {}}
          onClear={() => {}}
          onUploadKey={(key, meta) =>
            uploadDoc.mutate(
              { id: item.payableId, key, ...meta },
              {
                onSuccess: () => {
                  toast.success(t('pendencies.fiscal.attachSuccess'));
                  onClose();
                },
                onError: (e) => toast.error(getErrorMessage(e)),
              },
            )
          }
          variant='band'
        />
      </Panel>
    </Drawer>
  );
}

function InfoField({ label, value }: { label: string; value: string }) {
  return (
    <div className='info-card'>
      <div className='info-label'>{label}</div>
      <div className='info-value' style={{ fontSize: 15 }}>
        {value}
      </div>
    </div>
  );
}
