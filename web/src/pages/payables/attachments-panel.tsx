import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Eye, Trash2, Paperclip } from 'lucide-react';
import { type Payable, type PayableAttachment, type PayableAttachmentKind } from '../../api';
import { P, usePermissions } from '../../permissions';
import { useUploadPayableAttachment } from '../../queries';
import { Button, FilePicker, Panel, Select } from '../../ui';
import { getErrorMessage } from '../../apiError';
import { fileErrorKey } from '../../download';
import { downloadAttachment, previewAttachment } from './download';

/** Whitelist + limite espelham o backend (`FileContentValidator` + 25 MiB). */
const ACCEPT = '.png,.jpg,.jpeg,.webp,.gif,.pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.xml';
const MAX_BYTES = 25 * 1024 * 1024;
/** Kinds offered on upload — only `FiscalDocument` satisfies the NF gate at tax analysis / accounting. */
const ATTACHMENT_KINDS: PayableAttachmentKind[] = ['FiscalDocument', 'BankSlip', 'Invoice', 'Other'];

/** Attachments panel for the payable detail. Upload liberado em qualquer status (inclusive Paga): o número
 * do documento e a NF são registro fiscal que o Contábil pode completar depois do pagamento. `onDelete` abre a
 * confirmação de exclusão. */
export function AttachmentsPanel({
  payable: p,
  onDelete,
}: {
  payable: Payable;
  onDelete: (att: PayableAttachment) => void;
}) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canWrite = can(P.payablesWrite);
  const [err, setErr] = useState<string | null>(null);
  const [pendingKind, setPendingKind] = useState<PayableAttachmentKind | ''>('');
  const upload = useUploadPayableAttachment({ onError: (e) => setErr(getErrorMessage(e)) });

  const send = (f: File | null) => {
    if (!f) return;
    setErr(null);
    if (f.size > MAX_BYTES) {
      setErr(t('attachments.tooLarge'));
      return;
    }
    if (!pendingKind) {
      setErr(t('attachments.kindRequired'));
      return;
    }
    upload.mutate({ id: p.id, file: f, kind: pendingKind });
    setPendingKind('');
  };

  return (
    <Panel title={t('attachments.title')}>
      {canWrite && (
        <div style={{ padding: 16, display: 'flex', gap: 8, alignItems: 'center' }}>
          <Select
            value={pendingKind}
            aria-label={t('attachments.kindLabel')}
            style={{ width: 150 }}
            onChange={(e) => setPendingKind(e.target.value as PayableAttachmentKind)}
          >
            <option value=''>{t('attachments.kindPlaceholder')}</option>
            {ATTACHMENT_KINDS.map((k) => (
              <option key={k} value={k}>
                {t(`attachments.kinds.${k}`)}
              </option>
            ))}
          </Select>
          <FilePicker
            accept={ACCEPT}
            placeholder={upload.isPending ? t('attachments.uploading') : t('attachments.attach')}
            onSelect={send}
          />
        </div>
      )}
      {p.attachments.length === 0 ? (
        <div className='empty-state' style={{ padding: 24 }}>
          <div className='s'>{t('attachments.empty')}</div>
        </div>
      ) : (
        <table>
          <tbody>
            {p.attachments.map((att) => (
              <tr key={att.id}>
                <td>
                  <Paperclip style={{ width: 14, height: 14, verticalAlign: 'middle', marginRight: 6 }} />
                  {att.fileName}
                  <span
                    className='badge-status badge-doc'
                    style={{ marginLeft: 8, fontSize: 10 }}
                    title={t('attachments.kindLabel')}
                  >
                    {t(`attachments.kinds.${att.kind}`)}
                  </span>
                </td>
                <td className='td-action'>
                  <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                    <Button
                      icon={<Eye />}
                      onClick={() => {
                        setErr(null);
                        previewAttachment(p.id, att).catch((e) => setErr(t(fileErrorKey(e, 'attachments.viewError'))));
                      }}
                    >
                      {t('attachments.view')}
                    </Button>
                    <Button
                      icon={<Download />}
                      onClick={() => {
                        setErr(null);
                        downloadAttachment(p.id, att).catch((e) =>
                          setErr(t(fileErrorKey(e, 'attachments.downloadError'))),
                        );
                      }}
                    >
                      {t('attachments.download')}
                    </Button>
                    {canWrite && (
                      <Button icon={<Trash2 />} onClick={() => onDelete(att)}>
                        {t('attachments.remove')}
                      </Button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {err && <p className='create-err'>{err}</p>}
    </Panel>
  );
}
