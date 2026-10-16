import { useCallback, useEffect, useRef } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ChevronLeft } from 'lucide-react';
import { itemForPath } from './nav.config';

/**
 * Origin-aware navigation (AEL-885).
 *
 * The problem: detail/form pages used to hardcode their "back" target to the parent list
 * (`nav('/app/payables')`), so arriving from the Tesouraria, a notification, or a specific
 * tab/filter/page of the list always bounced the user to a fixed screen. The origin already lives
 * in the browser's navigation act — we just record it in `location.state.from` (the full in-app URL,
 * `pathname + search`) and read it back. React Router v7 keeps `location.state` in
 * `window.history.state`, so the origin survives an in-tab refresh; a fresh deep-link has no state and
 * falls back to the page's known parent list.
 */

/** Shape stamped into history state to remember which screen opened the current one. */
interface Origin {
  from?: string;
}

/**
 * Emitter side: returns a navigate fn that stamps the CURRENT url (path + query) as the origin, so the
 * destination's back control can return exactly here — with its tab, filters and page preserved.
 */
export function useOriginNav(): (to: string, opts?: { replace?: boolean }) => void {
  const nav = useNavigate();
  const loc = useLocation();
  const from = loc.pathname + loc.search;
  return useCallback((to, opts) => nav(to, { state: { from } as Origin, replace: opts?.replace }), [nav, from]);
}

/**
 * Declarative counterpart of useOriginNav for `<Link state={...}>`: the origin descriptor to hand a Link so
 * its target's back control returns to the current screen. Call at component top level (not inside a map).
 */
export function useOriginState(): { from: string } {
  const loc = useLocation();
  return { from: loc.pathname + loc.search };
}

/**
 * Back side: resolves the back target to the origin captured in history state, or `fallback` when there is
 * none (fresh deep-link / new tab). `label` names the destination screen ("Voltar para Contas a pagar")
 * when the target maps to a nav item (via itemForPath, the same map the breadcrumb uses); otherwise it is
 * the generic "Voltar" — so screens outside the side-nav (or record targets) stay generic.
 */
export function useBackNavigation(fallback: string): { to: string; goBack: () => void; label: string } {
  const nav = useNavigate();
  const { t } = useTranslation();
  const from = (useLocation().state as Origin | null)?.from;
  const to = from ?? fallback;
  const screen = itemForPath(to.split('?')[0]);
  const label = screen ? t('common.backTo', { screen: t(screen.labelKey) }) : t('common.back');
  return { to, label, goBack: useCallback(() => nav(to), [nav, to]) };
}

/** O conteúdo roteado vive num container `.content` com scroll próprio (o body é overflow:hidden) que
 * PERSISTE entre rotas — então um navigate in-app mantém a posição de scroll anterior. Chame após navegar
 * quando o destino deve abrir no topo (ex.: ao concluir uma decisão de triagem/aprovação). */
export function scrollContentToTop(): void {
  document.querySelector('.content')?.scrollTo({ top: 0 });
}

/** Ready-made back control, reusing the shared `.backlink` style. Names the origin screen when known. */
export function BackLink({ fallback }: { fallback: string }) {
  const { goBack, label } = useBackNavigation(fallback);
  return (
    <button type='button' className='backlink' onClick={goBack}>
      <ChevronLeft /> {label}
    </button>
  );
}

/**
 * Pagination that lives in the URL (`?page=`) instead of component state, so navigating into a detail and
 * back restores the exact page. Drop-in for `useState(1)`: returns `[page, setPage]`. Page 1 is encoded as
 * the absent param to keep urls clean; writes `replace` so paging doesn't stack history entries.
 */
export function usePageParam(): [number, (p: number) => void] {
  const [params, setParams] = useSearchParams();
  const raw = Number(params.get('page'));
  const page = Number.isInteger(raw) && raw >= 1 ? raw : 1;
  const setPage = useCallback(
    (p: number) =>
      setParams(
        (prev) => {
          if (p <= 1) prev.delete('page');
          else prev.set('page', String(p));
          return prev;
        },
        { replace: true },
      ),
    [setParams],
  );
  return [page, setPage];
}

/**
 * Resets pagination to 1 when a filter/scope actually changes — but NOT on the initial mount, so returning
 * to a list via "back" keeps the restored `?page=`. Replaces the common `useEffect(() => setPage(1), deps)`.
 */
export function useResetPageOnChange(setPage: (p: number) => void, deps: unknown[]): void {
  const mounted = useRef(false);
  useEffect(() => {
    if (!mounted.current) {
      mounted.current = true;
      return;
    }
    setPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
}
