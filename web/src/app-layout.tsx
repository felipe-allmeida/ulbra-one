import { useState, type ReactNode } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Menu } from 'lucide-react';
import { AppDataProvider, useApp } from './store';
import { ImpersonationProvider } from './impersonation';
import { RealtimeProvider } from './realtime/RealtimeProvider';
import { useClaritySession } from './analytics';
import { SideNav } from './components/SideNav';
import { Topbar } from './components/Topbar';
import { SidebarLayoutProvider } from './sidebar-layout';
import { domainForPath, itemForPath } from './nav.config';

/**
 * Composição do /app: providers + chrome. Vive fora de `App.tsx` (que importa toda página da app)
 * para que `app-layout.test.tsx` consiga montá-la sozinha — o `RealtimeProvider` PRECISA envolver o
 * `Shell`, e essa relação some sem barulho se alguém a mover.
 */
export function AppLayout() {
  return (
    <ImpersonationProvider>
      <AppDataProvider>
        <RealtimeProvider>
          <ClaritySession />
          <Shell>
            <KeyedOutlet />
          </Shell>
        </RealtimeProvider>
      </AppDataProvider>
    </ImpersonationProvider>
  );
}

/** Etiqueta a sessão do Clarity (rota mascarada + cargo + empresa). Não renderiza nada. */
function ClaritySession() {
  useClaritySession();
  return null;
}

/**
 * Remonta o conteúdo da rota ao trocar de empresa. Sem isso, queries com `keepPreviousData`
 * (useRequisitions, usePayables, useVendors…) mantêm os dados da empresa anterior como placeholder
 * na troca — `isLoading` fica false e o skeleton não aparece, deixando dados da empresa antiga na
 * tela até os novos chegarem. Remontar (key por empresa) zera para um carregamento limpo, então o
 * loading-state (skeleton/spinner) aparece de forma consistente em toda a app. Só o conteúdo da rota
 * remonta — o chrome (SideNav/Topbar) fica intacto.
 */
function KeyedOutlet() {
  const { empresaId } = useApp();
  return <Outlet key={empresaId || 'none'} />;
}

/* ===== SHELL (sidebar + topbar) ===== */
// Exported (not just used locally) so it can be rendered directly in tests — see Shell.test.tsx,
// which exercises the moved nav trigger without the rest of the route tree.
export function Shell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const [navOpen, setNavOpen] = useState(false);
  const { pathname } = useLocation();

  const domain = domainForPath(pathname);
  const item = itemForPath(pathname);
  // A route outside the nav map (e.g. Notificações) has no sub-item — show the domain alone
  // instead of repeating the label ("Financeiro / Financeiro").
  const crumb = item ? (
    <>
      {t(domain.titleKey ?? domain.labelKey)} <b>/ {t(item.labelKey)}</b>
    </>
  ) : (
    <b>{t(domain.titleKey ?? domain.labelKey)}</b>
  );

  return (
    // O provider envolve o Shell inteiro porque seus dois consumidores ficam em pontas opostas:
    // o gatilho na topbar, a alça na borda da sidebar.
    <SidebarLayoutProvider>
      <SideNav open={navOpen} onClose={() => setNavOpen(false)} />

      {/* The nav trigger: a single element, rendered once, positioned per breakpoint by CSS alone
          (see `.hamburger` in index.css) — never duplicated per band. Above 1024px it's hidden
          (rail + panel are always visible); from 641–1024px CSS holds it in the topbar's old
          top-left spot; at ≤640px CSS floats it bottom-right, clear of the thumb-unfriendly
          top-left corner and the iPhone home indicator (`env(safe-area-inset-bottom)`). It lives
          here, next to `SideNav`, instead of inside `Topbar` — trigger and panel are neighbours in
          code, as they already are on screen. */}
      <button className='hamburger' onClick={() => setNavOpen(true)} aria-label='Menu'>
        <Menu />
      </button>

      <main className='main'>
        <Topbar crumb={crumb} />
        <div className='content'>{children}</div>
      </main>
    </SidebarLayoutProvider>
  );
}
