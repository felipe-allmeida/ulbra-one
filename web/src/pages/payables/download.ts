import { api, auth, type PayableAttachment, type Payment } from '../../api';
import { getApiBaseUrl } from '../../config/env';

/**
 * The download endpoint requires a Bearer token, which a plain `<a href>` cannot
 * send. So we fetch the file with the Authorization header, turn the response into
 * an object-URL blob, and trigger a programmatic download.
 */
export async function downloadAttachment(payableId: string, att: PayableAttachment): Promise<void> {
  const url = `${getApiBaseUrl()}/payables/${payableId}/attachments/${att.id}`;
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = att.fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(objectUrl);
}

/**
 * Downloads a payment proof. Same authed-fetch→blob approach as the attachment
 * download: a plain `<a href>` can't send the Bearer token, so we fetch with the
 * Authorization header, turn the response into an object-URL blob, and trigger a
 * programmatic download.
 */
export async function downloadPaymentProof(payableId: string, payment: Payment): Promise<void> {
  const url = api.downloadPaymentProofUrl(payableId, payment.id);
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = `comprovante-${payment.id}`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(objectUrl);
}
