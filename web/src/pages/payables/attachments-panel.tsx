import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Eye, Trash2, Paperclip } from 'lucide-react';
import { type Payable, type PayableAttachment } from '../../api';
import { P, usePermissions } from '../../permissions';
import { Button, Panel } from '../../ui';
import { downloadAttachment, previewAttachment } from './download';

/** Attachments panel for the payable detail. `onDelete` opens the delete confirmation. */
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
  return (
    <Panel title={t('attachments.title')}>
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
                </td>
                <td className='td-action'>
                  <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                    <Button
                      icon={<Eye />}
                      onClick={() => {
                        setErr(null);
                        previewAttachment(p.id, att).catch(() => setErr(t('attachments.viewError')));
                      }}
                    >
                      {t('attachments.view')}
                    </Button>
                    <Button
                      icon={<Download />}
                      onClick={() => {
                        setErr(null);
                        downloadAttachment(p.id, att).catch(() => setErr(t('attachments.downloadError')));
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
