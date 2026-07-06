import { api, auth } from '../../../api';

/**
 * O endpoint CSV exige Bearer, que um `<a href>` puro não envia. Buscamos com o header Authorization,
 * transformamos em blob object-URL e disparamos o download programático (mesmo padrão de payables/download.ts).
 */
export async function downloadAssessmentCsv(params: { perApur: string; companyId?: string }): Promise<void> {
  const url = api.fiscalAssessmentCsvUrl(params);
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = `apuracao-retencoes-${params.perApur}.csv`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(objectUrl);
}
