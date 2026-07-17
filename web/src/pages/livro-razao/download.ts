import { api } from '../../api';
import { downloadWithAuth } from '../../download';

/** Downloads the Livro Razão CSV (endpoint requires Bearer, which a plain `<a href>` can't send). */
export async function downloadGeneralLedgerExport(params: {
  companyId?: string;
  ledgerAccountId?: string;
  root?: string;
  from?: string;
  to?: string;
  costCenterId?: string;
}): Promise<void> {
  await downloadWithAuth(api.generalLedgerExportUrl(params), 'livro-razao.csv');
}
