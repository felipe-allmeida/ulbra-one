import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FileUp } from 'lucide-react';
import { type PayableStageItem } from '../../api';
import { useUploadPayableAttachment } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Drawer, Panel, Button, Field, FilePicker } from '../../ui';
import { brl } from '../../lib';
import { fmtDate } from '../../datetime';

/**
 * Attaches a Nota Fiscal file to a payable stuck in PendingAccounting without one. The file is stored as a
 * PayableAttachment (POST /{id}/attachments) — NO parsing/OCR; it's kept as a document of record and shows up
 * on the payable detail. Attaching satisfies the "has NF" gate, so the payable leaves this list and can be
 * accounted; completing accounting itself stays in the Contabilidade tab of /app/payables.
 */
export function AttachNfDrawer({ item, onClose }: { item: PayableStageItem; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const upload = useUploadPayableAttachment();
  const [file, setFile] = useState<File | null>(null);

  const submit = () => {
    if (!file) return;
    upload.mutate(
      { id: item.payableId, file },
      {
        onSuccess: () => {
          toast.success(t('pendencies.fiscal.attachSuccess'));
          onClose();
        },
        onError: (e) => toast.error(getErrorMessage(e)),
      },
    );
  };

  return (
    <Drawer
      open
      onClose={onClose}
      title={t('pendencies.fiscal.drawerTitle')}
      sub={item.counterpartyName}
      footer={
        <Button variant='brand' icon={<FileUp />} disabled={!file || upload.isPending} onClick={submit}>
          {t('pendencies.fiscal.confirm')}
        </Button>
      }
    >
      <div className='info-grid'>
        <InfoField label={t('payables.columns.description')} value={item.description} />
        <InfoField label={t('payables.columns.amount')} value={brl(item.totalAmount)} />
        <InfoField label={t('payables.columns.dueDate')} value={fmtDate(item.firstDueDate)} />
        <InfoField label={t('payables.columns.installments')} value={String(item.installmentCount)} />
      </div>

      <Panel title={t('pendencies.fiscal.docTitle')}>
        <Field label={t('pendencies.fiscal.selectLabel')}>
          <FilePicker
            accept='.xml,application/xml,text/xml,.pdf,application/pdf'
            fileName={file?.name}
            placeholder={t('common.noFileSelected')}
            onSelect={setFile}
          />
        </Field>
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
