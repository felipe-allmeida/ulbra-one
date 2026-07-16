import { api } from '../../api';
import { downloadWithAuth } from '../../download';

/** Baixa o CSV do Livro Razão (o endpoint exige Bearer, que um `<a href>` puro não envia). */
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
