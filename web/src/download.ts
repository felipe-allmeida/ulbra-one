import { auth } from './api';

/** Error carrying the HTTP status of a failed protected-file fetch, so callers can show a
 *  status-specific message (403 = sem permissão; 404 = indisponível/fora do escopo). */
export class FileAccessError extends Error {
  constructor(public readonly status: number) {
    super(String(status));
    this.name = 'FileAccessError';
  }
}

/**
 * Maps a file-fetch failure to an i18n key. The cross-scope denial is intentionally masked by the API as
 * a 404 (AEL-764 anti-IDOR), so the 404 message stays neutral — it must never confirm the file exists.
 * Anything other than 403/404 falls back to the caller's generic key.
 */
export function fileErrorKey(e: unknown, fallbackKey: string): string {
  const status = e instanceof FileAccessError ? e.status : 0;
  if (status === 403) return 'files.forbidden';
  if (status === 404) return 'files.unavailable';
  return fallbackKey;
}

/** Extracts the download filename from a Content-Disposition header (RFC 5987 `filename*` wins over `filename`). */
function fileNameFromDisposition(header: string | null): string | undefined {
  if (!header) return undefined;
  const star = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(header);
  if (star?.[1]) {
    try {
      return decodeURIComponent(star[1].trim().replace(/^"|"$/g, ''));
    } catch {
      /* fall through to the plain filename */
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain?.[1]?.trim();
}

/**
 * Fetches a protected file with the Bearer token (a plain `<a href>` can't send it), turns the response into a
 * blob object-URL and triggers a programmatic download. Shared by the CSV/XML exports and the attachment/proof
 * downloads.
 *
 * When `fileName` is omitted, the name the server sends in Content-Disposition is used (so the download adopts
 * the backend's canonical file name, e.g. the stored remittance file name), falling back to `download.bin`.
 */
export async function downloadWithAuth(url: string, fileName?: string): Promise<void> {
  const headers = new Headers();
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
  const res = await fetch(url, { headers });
  if (!res.ok) throw new FileAccessError(res.status);
  const name = fileName ?? fileNameFromDisposition(res.headers.get('content-disposition')) ?? 'download.bin';
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = name;
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
    if (!res.ok) throw new FileAccessError(res.status);
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
