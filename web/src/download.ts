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

/**
 * Fetches a protected file with the Bearer token and opens it INLINE in a new tab (view without downloading).
 * The tab is opened synchronously in the click gesture so popup blockers don't kill it, then navigated to the
 * blob object-URL once the fetch resolves. Inline rendering (image/PDF/text) is up to the browser via blob MIME.
 */
export async function previewWithAuth(url: string): Promise<void> {
  const tab = window.open('about:blank', '_blank');
  try {
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(url, { headers });
    if (!res.ok) throw new Error(`${res.status}`);
    const objectUrl = URL.createObjectURL(await res.blob());
    if (tab) tab.location.href = objectUrl;
    else window.open(objectUrl, '_blank');
    // Give the new tab time to load before releasing the object-URL.
    setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
  } catch (e) {
    tab?.close();
    throw e;
  }
}
