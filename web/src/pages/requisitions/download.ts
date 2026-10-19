import { type RequisitionAttachment } from '../../api';
import { getApiBaseUrl } from '../../config/env';
import { downloadWithAuth, previewWithAuth } from '../../download';

/** Baixa um anexo da requisição (fetch autenticado → blob; `<a href>` puro não envia o Bearer). */
export async function downloadRequisitionAttachment(reqId: string, att: RequisitionAttachment): Promise<void> {
  await downloadWithAuth(`${getApiBaseUrl()}/requisitions/${reqId}/attachments/${att.id}`, att.fileName);
}

/** Abre um anexo inline em nova aba (fetch autenticado → blob) — visualização rápida. */
export async function previewRequisitionAttachment(reqId: string, att: RequisitionAttachment): Promise<void> {
  await previewWithAuth(`${getApiBaseUrl()}/requisitions/${reqId}/attachments/${att.id}`);
}
