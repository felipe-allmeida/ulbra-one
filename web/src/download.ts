import { auth } from './api';

/**
 * Fetches a protected file with the Bearer token (a plain `<a href>` can't send it), turns the response into a
 * blob object-URL and triggers a programmatic download. Shared by the CSV/XML exports and the attachment/proof
 * downloads.
 */
export async function downloadWithAuth(url: string, fileName: string): Promise<void> {
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
