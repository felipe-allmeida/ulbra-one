import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Eye, FileText, Paperclip, Trash2 } from 'lucide-react';
import { type Requisition, type RequisitionAttachment, type RequisitionAttachmentKind } from '../../api';
import { P, usePermissions } from '../../permissions';
import { useUpdateRequisitionAttachmentKind, useUploadRequisitionAttachment } from '../../queries';
import { Button, FilePicker, Panel, Select } from '../../ui';
import { getErrorMessage } from '../../apiError';
import { fileErrorKey } from '../../download';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
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

  // Ordem = a ordem já renderizada hoje (arquivo+selo, depois o cluster de ações) — a tabela nunca teve
  // <thead> (2 colunas plenamente auto-explicativas), então `hideHeader` evita acrescentar uma barra de
  // cabeçalho que não existia (ver ResponsiveTable.tsx). Mantém as DUAS colunas de hoje (`file` continua
  // com ícone+nome+selo juntos — uma 3ª coluna mudaria a estrutura desktop) mas o conteúdo do `file` virou
  // um flex row próprio (`minWidth: 0` no nome) em vez de depender do nowrap+ellipsis do `.rt-title`: um
  // nome de arquivo longo estourava a linha inteira e o `.rt-title` cortava o SELO junto com o final do
  // nome (o selo, vindo depois do texto, ficava fora da área visível — sem aviso, sem ellipsis nele).
  // Com o próprio nome truncando (elipse) dentro do seu flex-item e o selo com `flexShrink: 0`, o selo
  // sempre aparece inteiro. `actions` usa `header: ''` + `align:
  // 'right'` porque a coluna nunca teve cabeçalho visível e o `.td-action` original já alinhava à direita
  // (mesmo padrão de payables/attachments-panel.tsx — que tem o mesmo risco de selo cortado; ver o
  // follow-up aberto para lá).
  const attachmentColumns: Column<RequisitionAttachment>[] = [
    {
      key: 'file',
      header: t('requisitions.attachments.columns.file'),
      role: 'title',
      cell: (att) => (
        <span style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
          <Paperclip style={{ width: 14, height: 14, flexShrink: 0 }} />
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', minWidth: 0 }}>
            {att.fileName}
          </span>
          {canEditKind ? (
            // Inline reclassify: change a mis-typed attachment's kind in place (AEL-959).
            <Select
              value={att.kind}
              aria-label={t('requisitions.attachments.kindLabel')}
              disabled={updateKind.isPending}
              style={{ marginLeft: 'auto', flexShrink: 0, width: 120, height: 28, fontSize: 12, padding: '0 6px' }}
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
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (att) => (
        // Só-ícone: três rótulos repetidos por linha competiam com o nome do arquivo, que é o dado da
        // linha. O rótulo continua em `aria-label` (nome acessível intacto) e aparece no hover por `title`.
        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
          <Button
            className='btn-icon'
            icon={<Eye />}
            aria-label={t('requisitions.attachments.view')}
            title={t('requisitions.attachments.view')}
            onClick={() => {
              setErr(null);
              previewRequisitionAttachment(req.id, att).catch((e) =>
                setErr(t(fileErrorKey(e, 'requisitions.attachments.viewError'))),
              );
            }}
          />
          <Button
            className='btn-icon'
            icon={<Download />}
            aria-label={t('requisitions.attachments.download')}
            title={t('requisitions.attachments.download')}
            onClick={() => {
              setErr(null);
              downloadRequisitionAttachment(req.id, att).catch((e) =>
                setErr(t(fileErrorKey(e, 'requisitions.attachments.downloadError'))),
              );
            }}
          />
          {canWrite && (
            <Button
              className='btn-icon'
              icon={<Trash2 />}
              aria-label={t('requisitions.attachments.remove')}
              title={t('requisitions.attachments.remove')}
              onClick={() => onDelete(att)}
            />
          )}
        </div>
      ),
    },
  ];

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
          // flexWrap: a Select de largura fixa (150px) + o FilePicker cabem lado a lado no desktop; a 390px a
          // soma estoura a largura do painel — sem quebra de linha eles ficariam cortados por `.panel { overflow:
          // hidden }` (mesmo ajuste em payables/attachments-panel.tsx).
          <div style={{ padding: 16, display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
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
        <ResponsiveTable rows={req.attachments} columns={attachmentColumns} rowKey={(att) => att.id} hideHeader />
      )}
      {err && (
        <p className='create-err' style={{ padding: '0 16px 16px' }}>
          {err}
        </p>
      )}
    </Panel>
  );
}
