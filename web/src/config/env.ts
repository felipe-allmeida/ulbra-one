/**
 * Runtime + build-time environment resolution.
 *
 * The built image is configured at *container startup*: `docker-entrypoint.sh`
 * writes `/usr/share/nginx/html/env.js`, which sets `window.__ENV__`. We read
 * those values first so a single image can be deployed to any environment.
 *
 * Resolution order for each key:
 *   1. `window.__ENV__` (runtime — production container)
 *   2. `import.meta.env`  (build-time — `vite build` / local `vite dev`)
 *   3. hard-coded default
 *
 * In jsdom (vitest) and local `vite dev` there is no real runtime injection;
 * the `public/env.js` stub provides empty strings, so resolution falls through
 * to `import.meta.env` / the default. This keeps local dev, vitest and the
 * Playwright smoke green.
 */

declare global {
  interface Window {
    __ENV__?: {
      VITE_API_BASE_URL?: string;
      VITE_GOOGLE_CLIENT_ID?: string;
      VITE_ENVIRONMENT?: string;
    };
  }
}

/** Reads a runtime value, treating empty strings as "not set". */
function runtime(key: keyof NonNullable<Window['__ENV__']>): string | undefined {
  const value = typeof window !== 'undefined' ? window.__ENV__?.[key] : undefined;
  return value && value.trim() !== '' ? value : undefined;
}

export function getApiBaseUrl(): string {
  return runtime('VITE_API_BASE_URL') ?? import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5001/api';
}

export function getGoogleClientId(): string | undefined {
  return runtime('VITE_GOOGLE_CLIENT_ID') ?? import.meta.env.VITE_GOOGLE_CLIENT_ID;
}

/**
 * Ambiente lógico da SPA. Governa o login rápido por cargo na tela de login, então o default
 * precisa falhar FECHADO: um valor ausente não pode habilitar caminho de desenvolvimento.
 *
 * Antes o fallback era 'development', e como o Terraform nunca injetava VITE_ENVIRONMENT,
 * TODO deploy — inclusive produção — se declarava desenvolvimento e exibia os botões
 * "Entrar como Admin/Financeiro/…". Não era bypass (o backend responde 404 em /auth/dev-token
 * fora de Development), mas ficava a um env var errado de virar um.
 *
 * `import.meta.env.DEV` é do Vite e só é true no dev server; qualquer `vite build` gera false.
 * É o que preserva o login rápido localmente sem depender de configuração.
 */
export function getEnvironment(): string {
  return (
    runtime('VITE_ENVIRONMENT') ??
    import.meta.env.VITE_ENVIRONMENT ??
    (import.meta.env.DEV ? 'development' : 'production')
  );
}

export {};
