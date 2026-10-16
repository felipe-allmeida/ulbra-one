/**
 * Microsoft Clarity — carregamento e contexto de sessão.
 *
 * A tag vivia inline no `index.html` e carregava em qualquer host. O resultado: ~2/3 das sessões
 * coletadas vinham de `localhost:5173` e `dev.one.ulbra.ai`, então toda métrica de comportamento
 * (dead click, tempo ativo, scroll) chegava diluída com sessão de desenvolvimento. Trazer o
 * bootstrap pra cá resolve isso e, de quebra, dá acesso a `window.__ENV__` — que o script no
 * `<head>` não tinha, porque `env.js` só é carregado depois, no `<body>`.
 *
 * O que NÃO fazemos aqui, deliberadamente: `clarity('identify', …)`. Ele tornaria cada sessão
 * atribuível a um funcionário específico dentro de um serviço de terceiro. A pergunta que a
 * análise precisa responder é "de qual cargo é este clique morto", e `role` responde isso sem
 * identificar ninguém.
 */
import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { getEnvironment } from './config/env';
import { usePermissions } from './permissions';
import { useApp } from './store';

const PROJECT_ID = 'xmuysku8hd';
const TAG_SRC = `https://www.clarity.ms/tag/${PROJECT_ID}`;

type ClarityFn = (...args: unknown[]) => void;

declare global {
  interface Window {
    clarity?: ClarityFn;
  }
}

/**
 * Ids prefixados (`payable_<32 hex>`, gerados pelo source generator de typed ids) e UUIDs viram
 * `:id`. Sem isso cada registro vira uma "página" distinta no dashboard e não há agregação por
 * tela possível — além de mandar id de registro nosso pra dentro do Clarity.
 */
const PREFIXED_ID = /\b[a-z]+_[0-9a-f]{32}\b/gi;
const UUID = /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi;

export function maskRoute(pathname: string): string {
  return pathname.replace(PREFIXED_ID, ':id').replace(UUID, ':id');
}

/** Injeta a tag do Clarity — só em produção, e só uma vez. */
export function initClarity(): void {
  if (getEnvironment() !== 'production') return;
  if (typeof document === 'undefined') return;
  if (document.querySelector(`script[src="${TAG_SRC}"]`)) return;

  // Fila de chamadas: `clarity(...)` precisa aceitar argumentos antes de o script terminar de
  // carregar. É o mesmo shim do snippet oficial, só que tipado.
  window.clarity ??= function (...args: unknown[]) {
    (window.clarity as ClarityFn & { q?: unknown[][] }).q ??= [];
    (window.clarity as ClarityFn & { q?: unknown[][] }).q!.push(args);
  } as ClarityFn;

  const script = document.createElement('script');
  script.async = true;
  script.src = TAG_SRC;
  document.head.appendChild(script);
}

/** Tag customizada da sessão (cargo, empresa) — permite segmentar o dashboard. */
export function clarityTag(key: string, value: string): void {
  if (!value) return;
  window.clarity?.('set', key, value);
}

/** Rota corrente, com os ids mascarados. */
export function clarityRoute(pathname: string): void {
  window.clarity?.('set', 'route', maskRoute(pathname));
}

/**
 * Contexto da sessão: reporta a rota (mascarada) a cada navegação e etiqueta a sessão com cargo e
 * empresa. Chamado de dentro do layout autenticado, onde `me` e `empresaId` já existem.
 *
 * Sem essas duas tags o dashboard mostra 28 usuários indistinguíveis — dá pra ver que um clique
 * morreu, não de qual cargo ele veio, que é o que decide a prioridade da correção.
 */
export function useClaritySession(): void {
  const { pathname } = useLocation();
  const { me } = usePermissions();
  const { empresaId } = useApp();

  useEffect(() => clarityRoute(pathname), [pathname]);
  useEffect(() => clarityTag('role', me?.role ?? ''), [me?.role]);
  useEffect(() => clarityTag('company', empresaId), [empresaId]);
}
