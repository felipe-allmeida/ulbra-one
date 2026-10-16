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
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';

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

  // Ordem = a ordem já renderizada hoje (arquivo+selo, depois o cluster de ações) — a tabela nunca
  // teve <thead> (2 colunas plenamente auto-explicativas), então `hideHeader` evita acrescentar uma
  // barra de cabeçalho que não existia (ver ResponsiveTable.tsx). `file` mantém as DUAS colunas de hoje
  // (ícone+nome+selo continuam juntos — uma 3ª coluna mudaria a estrutura desktop), mas o conteúdo virou
  // um flex row próprio (`minWidth: 0` no nome) em vez de depender do nowrap+ellipsis do `.rt-title`:
  // um nome de arquivo longo estourava a linha inteira e o `.rt-title` cortava o SELO junto com o final
  // do nome — sem aviso, sem "…" nele (o mesmo defeito que a Task 16 já achou e consertou no irmão
  // requisitions/attachments-panel.tsx, commit a12209aa). Com o nome truncando sozinho (elipse) dentro
  // do seu flex-item e o selo com `flexShrink: 0`, o selo sempre aparece inteiro. `actions` usa
  // `header: ''` porque a coluna de ação nunca teve cabeçalho visível
  // (mesmo padrão de cash-flow.tsx/relacao-tab.tsx/remessas-titulos-tab.tsx).
  const columns: Column<PayableAttachment>[] = [
    {
      key: 'file',
      header: t('attachments.columns.file'),
      role: 'title',
      cell: (att) => (
        <span style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
          <Paperclip style={{ width: 14, height: 14, flexShrink: 0 }} />
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', minWidth: 0 }}>
            {att.fileName}
          </span>
          <span
            className='badge-status badge-doc'
            style={{ flexShrink: 0, fontSize: 10 }}
            title={t('attachments.kindLabel')}
          >
            {t(`attachments.kinds.${att.kind}`)}
          </span>
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (att) => (
        // Só-ícone, igual ao painel irmão em requisitions: o rótulo vive em `aria-label` (nome acessível
        // intacto) e reaparece no hover por `title`.
        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
          <Button
            className='btn-icon'
            icon={<Eye />}
            aria-label={t('attachments.view')}
            title={t('attachments.view')}
            onClick={() => {
              setErr(null);
              previewAttachment(p.id, att).catch((e) => setErr(t(fileErrorKey(e, 'attachments.viewError'))));
            }}
          />
          <Button
            className='btn-icon'
            icon={<Download />}
            aria-label={t('attachments.download')}
            title={t('attachments.download')}
            onClick={() => {
              setErr(null);
              downloadAttachment(p.id, att).catch((e) => setErr(t(fileErrorKey(e, 'attachments.downloadError'))));
            }}
          />
          {canWrite && (
            <Button
              className='btn-icon'
              icon={<Trash2 />}
              aria-label={t('attachments.remove')}
              title={t('attachments.remove')}
              onClick={() => onDelete(att)}
            />
          )}
        </div>
      ),
    },
  ];

  return (
    <Panel title={t('attachments.title')}>
      {canWrite && (
        // flexWrap: a Select de largura fixa (150px) + o FilePicker cabem lado a lado no desktop; a 390px
        // (Step 0 do procedimento canônico) a soma estoura a largura do painel — sem quebra de linha eles
        // ficariam cortados por `.panel { overflow: hidden }`.
        <div style={{ padding: 16, display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
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
        <ResponsiveTable rows={p.attachments} columns={columns} rowKey={(att) => att.id} hideHeader />
      )}
      {err && <p className='create-err'>{err}</p>}
    </Panel>
  );
}
