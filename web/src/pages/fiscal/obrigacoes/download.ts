import { api, auth } from '../../../api';

/**
 * O endpoint CSV exige Bearer, que um `<a href>` puro não envia. Buscamos com o header Authorization,
 * transformamos em blob object-URL e disparamos o download programático (mesmo padrão de payables/download.ts).
 */
export async function downloadAssessmentCsv(params: { perApur: string; companyId?: string }): Promise<void> {
  await downloadWithAuth(api.fiscalAssessmentCsvUrl(params), `apuracao-retencoes-${params.perApur}.csv`);
}

/** Baixa o XML da EFD-Reinf (R-4020) de uma empresa/competência. */
export async function downloadEfdReinfXml(params: { perApur: string; companyId: string }): Promise<void> {
  await downloadWithAuth(api.efdReinfXmlUrl(params), `efd-reinf-r4020-${params.perApur}.xml`);
}

/** fetch com Bearer → blob → download programático (o endpoint exige auth; `<a href>` não envia o token). */
async function downloadWithAuth(url: string, fileName: string): Promise<void> {
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(objectUrl);
}
