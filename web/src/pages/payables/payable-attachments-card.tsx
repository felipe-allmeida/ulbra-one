import { useTranslation } from 'react-i18next';
import { Eye, Download, Paperclip } from 'lucide-react';
import { type PayableAttachment } from '../../api';
import { Button } from '../../ui';
import { useToast } from '../../toast';
import { fileErrorKey } from '../../download';
import { previewAttachment, downloadAttachment } from './download';

/** Lists every attachment on the payable, each with a type badge, with inline view + download, for the accounting review. */
export function PayableAttachmentsCard({
  payableId,
  attachments,
  title,
  emptyLabel,
}: {
  payableId: string;
  attachments: readonly PayableAttachment[];
  title: string;
  emptyLabel: string;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  return (
    <div className='drawer-section'>
      <div className='section-title'>{title}</div>
      {attachments.length === 0 ? (
        <div className='help'>{emptyLabel}</div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {attachments.map((att) => (
            <div
              key={att.id}
              style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}
            >
              <span style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
                <Paperclip style={{ width: 14, height: 14, flexShrink: 0 }} />
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {att.fileName}
                </span>
                <span
                  className='badge-status badge-doc'
                  style={{ fontSize: 10, flexShrink: 0 }}
                  title={t('attachments.kindLabel')}
                >
                  {t(`attachments.kinds.${att.kind}`)}
                </span>
              </span>
              {/* Só-ícone (rótulo em aria-label/title), igual aos painéis de anexo: o dado da linha é o
                  nome do arquivo, e o rótulo repetido por linha disputava a largura com ele. */}
              <span style={{ display: 'flex', gap: 6, flexShrink: 0 }}>
                <Button
                  className='btn-icon'
                  icon={<Eye />}
                  aria-label={t('attachments.view')}
                  title={t('attachments.view')}
                  onClick={() =>
                    void previewAttachment(payableId, att).catch((e) =>
                      toast.error(t(fileErrorKey(e, 'attachments.viewError'))),
                    )
                  }
                />
                <Button
                  className='btn-icon'
                  icon={<Download />}
                  aria-label={t('attachments.download')}
                  title={t('attachments.download')}
                  onClick={() =>
                    void downloadAttachment(payableId, att).catch((e) =>
                      toast.error(t(fileErrorKey(e, 'attachments.downloadError'))),
                    )
                  }
                />
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
