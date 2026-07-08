import { api, type PayableAttachment, type Payment } from '../../api';
import { getApiBaseUrl } from '../../config/env';
import { downloadWithAuth } from '../../download';

/** Downloads a payable attachment (authed fetch → blob; a plain `<a href>` can't send the Bearer token). */
export async function downloadAttachment(payableId: string, att: PayableAttachment): Promise<void> {
  await downloadWithAuth(`${getApiBaseUrl()}/payables/${payableId}/attachments/${att.id}`, att.fileName);
}

/** Downloads a payment proof (same authed-fetch approach as the attachment download). */
export async function downloadPaymentProof(payableId: string, payment: Payment): Promise<void> {
  await downloadWithAuth(api.downloadPaymentProofUrl(payableId, payment.id), `comprovante-${payment.id}`);
}
