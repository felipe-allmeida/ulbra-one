import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  Wallet, Users, Target, ClipboardCheck, ArrowRight, Check,
  Building2, Zap, ShieldCheck, Calculator, Package,
} from 'lucide-react';
import { auth } from '../api';
import { brlCompact } from '../lib';

export function LandingPage() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const loggedIn = !!auth.token;
  const enter = () => nav(loggedIn ? '/app' : '/login');

  const features = [
    { icon: <Wallet />, k: 'finance' },
    { icon: <Users />, k: 'registry' },
    { icon: <Target />, k: 'costCenters' },
    { icon: <ClipboardCheck />, k: 'approvals' },
  ] as const;

  return (
    <div className="lp">
      {/* ===== NAV ===== */}
      <header className="lp-nav">
        <div className="lp-wrap lp-nav-inner">
          <a className="lp-brand" href="/"><span className="lp-logo">U1</span> {t('common.appName')}</a>
          <nav className="lp-nav-links">
            <a href="#features">{t('landing.nav.features')}</a>
            <a href="#modules">{t('landing.nav.modules')}</a>
          </nav>
          <button className="lp-btn lp-btn-brand" onClick={enter}>
            {loggedIn ? t('landing.cta.button') : t('common.enter')} <ArrowRight />
          </button>
        </div>
      </header>

      {/* ===== HERO ===== */}
      <section className="lp-hero">
        <div className="lp-blob lp-blob-a" />
        <div className="lp-blob lp-blob-b" />
        <div className="lp-wrap lp-hero-grid">
          <div className="lp-hero-copy">
            <span className="lp-eyebrow">{t('landing.hero.eyebrow')}</span>
            <h1 className="lp-display">{t('landing.hero.title')}</h1>
            <p className="lp-lead">{t('landing.hero.subtitle')}</p>
            <div className="lp-hero-cta">
              <button className="lp-btn lp-btn-brand lp-btn-lg" onClick={enter}>
                {t('landing.hero.ctaPrimary')} <ArrowRight />
              </button>
              <a className="lp-btn lp-btn-ghost lp-btn-lg" href="#features">{t('landing.hero.ctaSecondary')}</a>
            </div>
            <p className="lp-trust">{t('landing.hero.trust')}</p>
          </div>
          <HeroMock />
        </div>
      </section>

      {/* ===== FEATURES ===== */}
      <section className="lp-section" id="features">
        <div className="lp-wrap">
          <div className="lp-section-head">
            <h2 className="lp-h2">{t('landing.features.title')}</h2>
            <p className="lp-section-sub">{t('landing.features.subtitle')}</p>
          </div>
          <div className="lp-features">
            {features.map((f) => (
              <article className="lp-feature" key={f.k}>
                <div className="lp-feature-ic">{f.icon}</div>
                <h3>{t(`landing.features.${f.k}.title`)}</h3>
                <p>{t(`landing.features.${f.k}.body`)}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      {/* ===== SHOWCASE ===== */}
      <section className="lp-section lp-section-soft">
        <div className="lp-wrap lp-showcase">
          <div className="lp-showcase-copy">
            <span className="lp-eyebrow">{t('landing.showcase.eyebrow')}</span>
            <h2 className="lp-h2">{t('landing.showcase.title')}</h2>
            <p className="lp-section-sub">{t('landing.showcase.body')}</p>
            <ul className="lp-checklist">
              {(['multiCompany', 'realtime', 'audit'] as const).map((p) => (
                <li key={p}><span className="lp-check"><Check /></span>{t(`landing.showcase.points.${p}`)}</li>
              ))}
            </ul>
          </div>
          <ShowcaseMock />
        </div>
      </section>

      {/* ===== MODULES ===== */}
      <section className="lp-section" id="modules">
        <div className="lp-wrap">
          <div className="lp-section-head">
            <h2 className="lp-h2">{t('landing.modules.title')}</h2>
            <p className="lp-section-sub">{t('landing.modules.subtitle')}</p>
          </div>
          <div className="lp-modules">
            <ModuleCard icon={<Wallet />} name={t('nav.sections.finance')} state={t('landing.modules.available')} available />
            <ModuleCard icon={<Users />} name={t('nav.hr')} state={t('common.soon')} />
            <ModuleCard icon={<Calculator />} name={t('nav.accounting')} state={t('common.soon')} />
            <ModuleCard icon={<Package />} name={t('nav.supplies')} state={t('common.soon')} />
          </div>
        </div>
      </section>

      {/* ===== CTA ===== */}
      <section className="lp-cta">
        <div className="lp-wrap lp-cta-inner">
          <h2 className="lp-display lp-cta-title">{t('landing.cta.title')}</h2>
          <p>{t('landing.cta.subtitle')}</p>
          <button className="lp-btn lp-btn-light lp-btn-lg" onClick={enter}>
            {t('landing.cta.button')} <ArrowRight />
          </button>
        </div>
      </section>

      {/* ===== FOOTER ===== */}
      <footer className="lp-footer">
        <div className="lp-wrap lp-footer-inner">
          <a className="lp-brand" href="/"><span className="lp-logo">U1</span> {t('common.appName')}</a>
          <span className="lp-footer-tag">{t('landing.footer.tagline')}</span>
          <span className="lp-footer-rights">{t('landing.footer.rights', { year: 2026 })}</span>
        </div>
      </footer>
    </div>
  );
}

function ModuleCard({ icon, name, state, available }: { icon: React.ReactNode; name: string; state: string; available?: boolean }) {
  return (
    <div className={`lp-module ${available ? 'is-available' : ''}`}>
      <div className="lp-module-ic">{icon}</div>
      <div className="lp-module-name">{name}</div>
      <span className={`lp-module-state ${available ? 'on' : ''}`}>{state}</span>
    </div>
  );
}

/* Stylized product mock — CSS-only, no screenshot. */
function HeroMock() {
  const { t } = useTranslation();
  return (
    <div className="lp-mock" aria-hidden>
      <div className="lp-mock-top">
        <span className="lp-dot" /><span className="lp-dot" /><span className="lp-dot" />
      </div>
      <div className="lp-mock-body">
        <div className="lp-mock-kpis">
          <Kpi label={t('landing.mock.balance')} value={brlCompact(1_284_500)} tone="brand" />
          <Kpi label={t('landing.mock.payable')} value={brlCompact(312_900)} tone="danger" />
          <Kpi label={t('landing.mock.receivable')} value={brlCompact(498_200)} tone="success" />
          <Kpi label={t('landing.mock.pending')} value="7" tone="warning" />
        </div>
        <div className="lp-mock-panel">
          <div className="lp-mock-panel-title">{t('landing.mock.budget')}</div>
          {[['Acadêmico', 78], ['Infraestrutura', 61], ['Marketing', 44], ['TI', 33]].map(([name, pct]) => (
            <div className="lp-mock-bar-row" key={name as string}>
              <span>{name}</span>
              <div className="lp-mock-bar"><div className="lp-mock-bar-fill" style={{ width: `${pct}%` }} /></div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function Kpi({ label, value, tone }: { label: string; value: string; tone: string }) {
  return (
    <div className={`lp-mock-kpi tone-${tone}`}>
      <div className="lp-mock-kpi-label">{label}</div>
      <div className="lp-mock-kpi-value">{value}</div>
    </div>
  );
}

function ShowcaseMock() {
  return (
    <div className="lp-showcase-mock" aria-hidden>
      <div className="lp-float lp-float-1"><Building2 /><div><strong>4 empresas</strong><span>multi-CNPJ</span></div></div>
      <div className="lp-float lp-float-2"><Zap /><div><strong>Auto-aprovado</strong><span>≤ limite do CC</span></div></div>
      <div className="lp-float lp-float-3"><ShieldCheck /><div><strong>Auditoria</strong><span>quem · quando</span></div></div>
    </div>
  );
}
