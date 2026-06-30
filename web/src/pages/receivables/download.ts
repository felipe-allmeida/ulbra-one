import { api, auth, type Receipt } from '../../api';

/** Receipt proof download (authed fetch → blob). */
export async function downloadReceiptProof(receivableId: string, receipt: Receipt): Promise<void> {
  const url = api.downloadReceiptProofUrl(receivableId, receipt.id);
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = `comprovante-${receipt.id}`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(objectUrl);
}
