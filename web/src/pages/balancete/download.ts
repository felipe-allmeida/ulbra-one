import { api } from '../../api';
import { downloadWithAuth } from '../../download';

/** Downloads the Balancete CSV (endpoint requires Bearer, which a plain `<a href>` can't send). */
export async function downloadTrialBalanceExport(params: {
  companyId?: string;
  from?: string;
  to?: string;
  costCenterId?: string;
  unitId?: string;
}): Promise<void> {
  await downloadWithAuth(api.trialBalanceExportUrl(params), 'balancete.csv');
}
