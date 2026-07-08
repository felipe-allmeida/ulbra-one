import { api } from '../../../api';
import { downloadWithAuth } from '../../../download';

/** Baixa o CSV da apuração (o endpoint exige Bearer, que um `<a href>` puro não envia). */
export async function downloadAssessmentCsv(params: { perApur: string; companyId?: string }): Promise<void> {
  await downloadWithAuth(api.fiscalAssessmentCsvUrl(params), `apuracao-retencoes-${params.perApur}.csv`);
}

/** Baixa o XML da EFD-Reinf (R-4020) de uma empresa/competência. */
export async function downloadEfdReinfXml(params: { perApur: string; companyId: string }): Promise<void> {
  await downloadWithAuth(api.efdReinfXmlUrl(params), `efd-reinf-r4020-${params.perApur}.xml`);
}
