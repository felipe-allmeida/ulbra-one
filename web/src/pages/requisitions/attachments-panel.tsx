import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Eye, FileText, Paperclip, Trash2 } from 'lucide-react';
import { type Requisition, type RequisitionAttachment, type RequisitionAttachmentKind } from '../../api';
import { P, usePermissions } from '../../permissions';
import { useUpdateRequisitionAttachmentKind, useUploadRequisitionAttachment } from '../../queries';
import { Button, FilePicker, Panel, Select } from '../../ui';
import { getErrorMessage } from '../../apiError';
import { fileErrorKey } from '../../download';
import { downloadRequisitionAttachment, previewRequisitionAttachment } from './download';

/** Whitelist + limite espelham o backend (`FileContentValidator` + 25 MiB). */
const ACCEPT = '.png,.jpg,.jpeg,.webp,.gif,.pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.xml';
const MAX_BYTES = 25 * 1024 * 1024;
/** Types the requester picks per file (only `FiscalDocument` satisfies the NF gate). */
const ATTACHMENT_KINDS: RequisitionAttachmentKind[] = ['FiscalDocument', 'BankSlip', 'Invoice', 'Other'];

/** Anexos da requisição (só populados no detalhe). Upload/exclusão liberados em qualquer status. */
export function AttachmentsPanel({
  req,
  onDelete,
  title,
  uploadLocked,
}: {
  req: Requisition;
  onDelete: (att: RequisitionAttachment) => void;
  /** Override do título do painel (ex.: "Documento fiscal ou fatura" no complemento financeiro). */
  title?: string;
  /** Bloqueia o upload geral (Conta Fixa em pagamento): a NF sobe pelo form "Lançar fatura do mês", num único
   * lugar, indo direto ao Payable do ciclo (AEL-877). A lista/preview de anexos existentes segue visível. */
  uploadLocked?: boolean;
}) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canWrite = can(P.requisitionsWrite);
  // Reclassifying an existing attachment's type is open to the requester (write) AND the controllership
  // (triage), so both can fix a mis-typed file while editing/triaging the requisition (AEL-959).
  const canEditKind = canWrite || can(P.requisitionsTriage);
  const [err, setErr] = useState<string | null>(null);
  const [pendingKind, setPendingKind] = useState<RequisitionAttachmentKind | ''>('');
  const upload = useUploadRequisitionAttachment({
    onError: (e) => setErr(getErrorMessage(e)),
  });
  const updateKind = useUpdateRequisitionAttachmentKind({ onError: (e) => setErr(getErrorMessage(e)) });

  const send = (f: File | null, kind?: RequisitionAttachmentKind) => {
    if (!f) return;
    setErr(null);
    if (f.size > MAX_BYTES) {
      setErr(t('requisitions.attachments.tooLarge'));
      return;
    }
    upload.mutate({ id: req.id, file: f, kind });
  };

  return (
    <Panel title={title ?? t('requisitions.attachments.title')}>
      {/* NF pendente: the requisition already generated a Payable but has no Nota Fiscal yet — the requester
          sends it here (marked FiscalDocument so it clears the pendency and propagates to the Payable). */}
      {canWrite && req.fiscalDocumentPending && (
        <div className='nf-callout' style={{ margin: 16 }}>
          <div className='nf-callout-head'>
            <FileText style={{ width: 16, height: 16 }} />
            <strong>{t('requisitions.fiscalPending.calloutTitle')}</strong>
          </div>
          <p className='nf-callout-sub'>{t('requisitions.fiscalPending.calloutSub')}</p>
          <FilePicker
            accept='.xml,application/xml,text/xml,.pdf,application/pdf'
            placeholder={
              upload.isPending ? t('requisitions.attachments.uploading') : t('requisitions.fiscalPending.send')
            }
            onSelect={(f) => send(f, 'FiscalDocument')}
          />
        </div>
      )}
      {canWrite &&
        (uploadLocked ? (
          <div style={{ padding: 16 }}>
            <p className='fc-hint' style={{ margin: 0 }}>
              {t('requisitions.attachments.lockedToInvoice')}
            </p>
          </div>
        ) : (
          <div style={{ padding: 16, display: 'flex', gap: 8, alignItems: 'center' }}>
            {/* Kind is mandatory before attaching: only FiscalDocument satisfies the NF gate in Tax/Accounting. */}
            <Select
              value={pendingKind}
              aria-label={t('requisitions.attachments.kindLabel')}
              style={{ width: 150 }}
              onChange={(e) => setPendingKind(e.target.value as RequisitionAttachmentKind)}
            >
              <option value=''>{t('requisitions.attachments.kindPlaceholder')}</option>
              {ATTACHMENT_KINDS.map((k) => (
                <option key={k} value={k}>
                  {t(`requisitions.attachments.kinds.${k}`)}
                </option>
              ))}
            </Select>
            <FilePicker
              accept={ACCEPT}
              placeholder={
                upload.isPending ? t('requisitions.attachments.uploading') : t('requisitions.attachments.add')
              }
              onSelect={(f) => {
                if (!pendingKind) {
                  setErr(t('requisitions.attachments.kindRequired'));
                  return;
                }
                send(f, pendingKind);
                setPendingKind('');
              }}
            />
          </div>
        ))}
      {req.attachments.length === 0 ? (
        <div className='empty-state' style={{ padding: 24 }}>
          <div className='s'>{t('requisitions.attachments.empty')}</div>
        </div>
      ) : (
        <table>
          <tbody>
            {req.attachments.map((att) => (
              <tr key={att.id}>
                <td>
                  {/* Clip + name (single line, truncates) + kind to the right; keep them vertically aligned so a
                      long filename never pushes the paperclip onto its own line. */}
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                    <Paperclip style={{ width: 14, height: 14, flexShrink: 0 }} />
                    <span
                      title={att.fileName}
                      style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', minWidth: 0 }}
                    >
                      {att.fileName}
                    </span>
                    {canEditKind ? (
                      // Inline reclassify: change a mis-typed attachment's kind in place (AEL-959).
                      <Select
                        value={att.kind}
                        aria-label={t('requisitions.attachments.kindLabel')}
                        disabled={updateKind.isPending}
                        style={{
                          marginLeft: 'auto',
                          flexShrink: 0,
                          width: 120,
                          height: 28,
                          fontSize: 12,
                          padding: '0 6px',
                        }}
                        onChange={(e) => {
                          setErr(null);
                          updateKind.mutate({
                            id: req.id,
                            attachmentId: att.id,
                            kind: e.target.value as RequisitionAttachmentKind,
                          });
                        }}
                      >
                        {ATTACHMENT_KINDS.map((k) => (
                          <option key={k} value={k}>
                            {t(`requisitions.attachments.kinds.${k}`)}
                          </option>
                        ))}
                      </Select>
                    ) : (
                      <span
                        className='badge-status badge-doc'
                        style={{ marginLeft: 'auto', flexShrink: 0, fontSize: 10 }}
                        title={t('requisitions.attachments.kindLabel')}
                      >
                        {t(`requisitions.attachments.kinds.${att.kind}`)}
                      </span>
                    )}
                  </div>
                </td>
                <td className='td-action'>
                  {/* Icon-only actions: the label lives in title/aria-label (native hover tooltip + a11y). */}
                  <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                    <Button
                      icon={<Eye />}
                      title={t('requisitions.attachments.view')}
                      aria-label={t('requisitions.attachments.view')}
                      style={{ padding: 7 }}
                      onClick={() => {
                        setErr(null);
                        previewRequisitionAttachment(req.id, att).catch((e) =>
                          setErr(t(fileErrorKey(e, 'requisitions.attachments.viewError'))),
                        );
                      }}
                    />
                    <Button
                      icon={<Download />}
                      title={t('requisitions.attachments.download')}
                      aria-label={t('requisitions.attachments.download')}
                      style={{ padding: 7 }}
                      onClick={() => {
                        setErr(null);
                        downloadRequisitionAttachment(req.id, att).catch((e) =>
                          setErr(t(fileErrorKey(e, 'requisitions.attachments.downloadError'))),
                        );
                      }}
                    />
                    {canWrite && (
                      <Button
                        icon={<Trash2 />}
                        title={t('requisitions.attachments.remove')}
                        aria-label={t('requisitions.attachments.remove')}
                        style={{ padding: 7 }}
                        onClick={() => onDelete(att)}
                      />
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {err && (
        <p className='create-err' style={{ padding: '0 16px 16px' }}>
          {err}
        </p>
      )}
    </Panel>
  );
}
