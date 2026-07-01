import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { ArrowRight, ChevronLeft } from 'lucide-react';
import { api, auth } from '../api';
import { GoogleSignInButton } from './google-signin';

export function LoginPage() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? '/app';

  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const res = await api.devLogin(email.trim() || 'dev@ulbra.br', name.trim() || 'Usuário Dev');
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

          <div className='auth-divider'>
            <span>{t('login.orDivider')}</span>
          </div>

          <form onSubmit={onSubmit} className='auth-fields'>
            <label className='auth-field'>
              <span>{t('login.emailLabel')}</span>
              <input
                type='email'
                autoFocus
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder={t('login.emailPlaceholder')}
                autoComplete='email'
              />
            </label>
            <label className='auth-field'>
              <span>
                {t('login.nameLabel')} <em>{t('login.nameOptional')}</em>
              </span>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t('login.namePlaceholder')}
                autoComplete='name'
              />
            </label>
            {error && <div className='auth-error'>{error}</div>}
            <button type='submit' className='auth-submit' disabled={busy}>
              {busy ? (
                t('login.submitting')
              ) : (
                <>
                  {t('login.submit')} <ArrowRight />
                </>
              )}
            </button>
          </form>

          <p className='auth-devnote'>{t('login.devNote')}</p>
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
