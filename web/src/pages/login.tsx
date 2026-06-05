import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { ArrowRight, ChevronLeft } from 'lucide-react';
import { api, auth } from '../api';

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

          <button type='button' className='auth-google' disabled title={t('common.soon')}>
            <GoogleMark /> {t('login.google')} <span className='auth-soon'>{t('common.soon')}</span>
          </button>

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
