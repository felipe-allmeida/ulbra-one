import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getGoogleClientId } from '../config/env';

const GIS_SRC = 'https://accounts.google.com/gsi/client';

/**
 * `renderButton` only takes a pixel width — there is no "100%" — and Google clamps it to
 * this range. So the button is sized from the container and re-rendered when it resizes,
 * otherwise a fixed width overflows the login card on a narrow phone (a 320px viewport
 * leaves the card 272px wide). The fallback covers environments with no layout (jsdom).
 */
const GIS_MIN_WIDTH = 200;
const GIS_MAX_WIDTH = 400;
const GIS_FALLBACK_WIDTH = 320;

interface CredentialResponse {
  credential?: string;
}

interface GoogleIdApi {
  initialize(config: { client_id: string; callback: (response: CredentialResponse) => void }): void;
  renderButton(parent: HTMLElement, options: Record<string, unknown>): void;
}

declare global {
  interface Window {
    google?: { accounts?: { id?: GoogleIdApi } };
  }
}

/** Loads the Google Identity Services script once and resolves with its `id` API. */
function loadGis(): Promise<GoogleIdApi> {
  return new Promise((resolve, reject) => {
    const ready = window.google?.accounts?.id;
    if (ready) return resolve(ready);

    const onLoad = () => {
      const api = window.google?.accounts?.id;
      if (api) resolve(api);
      else reject(new Error('Google Identity Services unavailable after load.'));
    };
    const onError = () => reject(new Error('Failed to load Google Identity Services.'));

    const existing = document.querySelector<HTMLScriptElement>(`script[src="${GIS_SRC}"]`);
    if (existing) {
      existing.addEventListener('load', onLoad);
      existing.addEventListener('error', onError);
      return;
    }
    const script = document.createElement('script');
    script.src = GIS_SRC;
    script.async = true;
    script.defer = true;
    script.addEventListener('load', onLoad);
    script.addEventListener('error', onError);
    document.head.appendChild(script);
  });
}

/**
 * Renders the official Google Sign-In button. On a successful sign-in it hands the
 * Google ID token (the `credential`) to `onCredential` — the caller exchanges it for
 * our JWT via `POST /auth/google`.
 *
 * When `VITE_GOOGLE_CLIENT_ID` is not configured (or the GIS script fails to load) it
 * falls back to the disabled "em breve" affordance so the page never breaks.
 */
export function GoogleSignInButton({ onCredential }: { onCredential: (idToken: string) => void }) {
  const { t } = useTranslation();
  const clientId = getGoogleClientId();
  const containerRef = useRef<HTMLDivElement>(null);
  const [failed, setFailed] = useState(false);
  const [width, setWidth] = useState(0);

  // Keep the latest callback without re-initializing GIS on every render.
  const callbackRef = useRef(onCredential);
  callbackRef.current = onCredential;

  // Track the container so the button follows the column across rotation and resize.
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const measure = () => setWidth(el.clientWidth || GIS_FALLBACK_WIDTH);
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    return () => observer.disconnect();
  }, [clientId, failed]);

  useEffect(() => {
    if (!clientId || !width) return;
    let cancelled = false;
    loadGis()
      .then((id) => {
        if (cancelled || !containerRef.current) return;
        id.initialize({
          client_id: clientId,
          callback: (response) => {
            if (response.credential) callbackRef.current(response.credential);
          },
        });
        id.renderButton(containerRef.current, {
          theme: 'outline',
          size: 'large',
          width: Math.round(Math.min(GIS_MAX_WIDTH, Math.max(GIS_MIN_WIDTH, width))),
          text: 'continue_with',
        });
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
    };
  }, [clientId, width]);

  if (!clientId || failed) {
    return (
      <button type='button' className='auth-google' disabled title={t('common.soon')}>
        <GoogleMark /> {t('login.google')} <span className='auth-soon'>{t('common.soon')}</span>
      </button>
    );
  }

  return <div ref={containerRef} className='auth-google-gis' />;
}

function GoogleMark() {
  return (
    <svg width='16' height='16' viewBox='0 0 48 48' aria-hidden>
      <path
        fill='#EA4335'
        d='M24 9.5c3.5 0 6.6 1.2 9 3.6l6.7-6.7C35.6 2.6 30.2.5 24 .5 14.6.5 6.5 5.9 2.6 13.7l7.8 6.1C12.3 13.8 17.7 9.5 24 9.5z'
      />
      <path
        fill='#4285F4'
        d='M46.5 24.5c0-1.6-.1-3.1-.4-4.5H24v9h12.7c-.6 3-2.3 5.5-4.8 7.2l7.5 5.8C43.9 38 46.5 31.8 46.5 24.5z'
      />
      <path
        fill='#FBBC05'
        d='M10.4 28.8c-.5-1.5-.8-3.1-.8-4.8s.3-3.3.8-4.8l-7.8-6.1C.9 16.2 0 20 0 24s.9 7.8 2.6 11l7.8-6.2z'
      />
      <path
        fill='#34A853'
        d='M24 47.5c6.2 0 11.4-2 15.2-5.5l-7.5-5.8c-2 1.4-4.7 2.3-7.7 2.3-6.3 0-11.7-4.3-13.6-10.1l-7.8 6.2C6.5 42.1 14.6 47.5 24 47.5z'
      />
    </svg>
  );
}
