import { api, type Receipt } from '../../api';
import { downloadWithAuth } from '../../download';

/** Receipt proof download (authed fetch → blob). */
export async function downloadReceiptProof(receivableId: string, receipt: Receipt): Promise<void> {
  await downloadWithAuth(api.downloadReceiptProofUrl(receivableId, receipt.id), `comprovante-${receipt.id}`);
}
