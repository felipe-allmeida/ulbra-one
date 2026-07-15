import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { ArrowRight, ChevronLeft } from 'lucide-react';
import { api, auth } from '../api';
import { GoogleSignInButton } from './google-signin';
import { getEnvironment } from '../config/env';

/**
 * Contas locais de teste (uma por cargo) semeadas pelo backend em Development
 * (`DevDataSeeder.SeedLocalUsersAsync`). Os botões só aparecem quando o ambiente
 * é `development` (local ou o container de dev no Azure), onde `/auth/dev-token`
 * está habilitado. Fora disso, apenas o login Google é exibido.
 */
const DEV_ACCOUNTS = [
  { label: 'Entrar como Admin', email: 'admin@ulbra.local', name: 'Admin (dev)' },
  { label: 'Entrar como Analista Financeiro', email: 'financeiro@ulbra.local', name: 'Financeiro (dev)' },
  { label: 'Entrar como Controladoria', email: 'controladoria@ulbra.local', name: 'Controladoria (dev)' },
  { label: 'Entrar como Analista Contábil', email: 'contabil@ulbra.local', name: 'Contábil (dev)' },
];

export function LoginPage() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? '/app';
  const isDev = getEnvironment() === 'development';

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function quickLogin(email: string, name: string) {
    setBusy(true);
    setError(null);
    try {
      const res = await api.devLogin(email, name);
      auth.set(res.token);
      nav(from, { replace: true });
    } catch {
      setError(t('login.error'));
      setBusy(false);
    }
  }

  async function onGoogleCredential(idToken: string) {
    setBusy(true);
    setError(null);
    try {
      const res = await api.googleSignIn(idToken);
      auth.set(res.token);
      nav(from, { replace: true });
    } catch {
      setError(t('login.error'));
      setBusy(false);
    }
  }

  return (
    <div className='auth'>
      {/* form */}
      <div className='auth-form-side'>
        <Link className='auth-back' to='/'>
          <ChevronLeft /> {t('login.backToSite')}
        </Link>
        <div className='auth-card'>
          <a className='auth-brand' href='/'>
            <span className='lp-logo'>U1</span> {t('common.appName')}
          </a>
          <h1 className='auth-title'>{t('login.title')}</h1>
          <p className='auth-sub'>{t('login.subtitle')}</p>

          <GoogleSignInButton onCredential={onGoogleCredential} />

          {isDev && (
            <>
              <div className='auth-divider'>
                <span>{t('login.orDivider')}</span>
              </div>
              <div className='auth-fields'>
                {DEV_ACCOUNTS.map((acc) => (
                  <button
                    key={acc.email}
                    type='button'
                    className='auth-submit'
                    disabled={busy}
                    onClick={() => quickLogin(acc.email, acc.name)}
                  >
                    {acc.label} <ArrowRight />
                  </button>
                ))}
              </div>
              <p className='auth-devnote'>Ambiente de desenvolvimento — login rápido por cargo.</p>
            </>
          )}

          {error && <div className='auth-error'>{error}</div>}
        </div>
      </div>

      {/* brand panel */}
      <div className='auth-brand-side'>
        <div className='auth-brand-blob' />
        <div className='auth-brand-content'>
          <div className='auth-brand-logo'>
            <span className='lp-logo lp-logo-lg'>U1</span> {t('common.appName')}
          </div>
          <p className='auth-brand-tag'>{t('login.brandTagline')}</p>
        </div>
      </div>
    </div>
  );
}
