# web/ — Frontend Conventions

Single-page React app for Ulbra.One (the ERP frontend). Lives in `web/` of a
polyglot repo (.NET backend at the root). Its own `package.json` + pnpm.

## Stack

- **React 19** + **Vite 6** + **TypeScript** (strict)
- **react-router-dom 7** for routing
- **react-i18next** / **i18next** for translations
- **Tailwind CSS v4** (via `@tailwindcss/vite`) — the app mostly uses a
  hand-written `src/index.css` with design tokens; Tailwind is available for
  utility classes.
- **lucide-react** for icons
- **Vitest** + **Testing Library** (jsdom) for tests

## Scripts

Run from `web/` (pnpm):

| Script              | What it does                                            |
| ------------------- | ------------------------------------------------------- |
| `pnpm dev`          | Vite dev server                                         |
| `pnpm build`        | `tsc --noEmit` then `vite build`                        |
| `pnpm preview`      | Preview the production build                            |
| `pnpm typecheck`    | `tsc --noEmit`                                          |
| `pnpm test`         | Vitest (run once)                                       |
| `pnpm test:watch`   | Vitest watch mode                                       |
| `pnpm lint`         | ESLint                                                  |
| `pnpm lint:fix`     | ESLint with `--fix`                                     |
| `pnpm format`       | Prettier `--write` across `web/`                        |
| `pnpm format:check` | Prettier `--check` (CI-safe)                            |
| `pnpm checks`       | `format:check` + `lint` + `typecheck` (run before push) |

CI (`.github/workflows/web-ci.yml`) runs typecheck, lint, test, and build on any
change under `web/**`.

## Code English / UI pt-BR

- **All code is English**: identifiers, file names, comments, types, commit
  messages. Domain entities use English names (Vendor / Customer / Employee,
  Company / Unit / CostCenter, Transaction, Finance, Registry).
- **All user-facing text is pt-BR**, and must go through i18n — never hardcode
  Portuguese strings in JSX.

## i18n rule

- Translations live in `src/i18n/`. `src/i18n/index.ts` registers resources and
  sets the default + fallback language to **pt-BR** (`src/i18n/locales/pt-BR.ts`).
- **Keys are English**; the pt-BR bundle is the single source of truth for the
  resource shape. New languages add a sibling bundle with the same keys.
- New screens **must not hardcode** UI text — use `useTranslation()` / `t('key')`.

## Folder layout (`src/`)

- `pages/` — route-level screens (dashboard, lancamentos, centros, estrutura,
  fornecedores, clientes, colaboradores, login, landing, plus `shared.tsx`).
- `ui.tsx` — shared UI primitives (buttons, inputs, drawer, dialogs, etc.).
- `toast.tsx` — toast notifications. `useAsync.ts` — async state helper.
- `store.tsx` — app-level data context (`AppDataProvider`).
- `api.ts` — backend API client + `auth` helpers.
- `format.ts` / `lib.ts` — formatting and small utilities (BRL, CPF/CNPJ, etc.).
- `i18n/` — i18n setup and locale bundles.
- Tests are colocated (`*.test.ts` / `*.test.tsx`).

## Routing

`react-router-dom` (`src/App.tsx`):

- `/` — landing page
- `/login` — login
- `/app/*` — authenticated area (gated): `inicio`, `contas-pagar`,
  `contas-receber`, `aprovacoes`, `lancamentos/*`, `centros/*`, `estrutura`,
  `fornecedores`, `clientes`, `colaboradores`. Unknown `/app/*` redirects to
  `inicio`; any other unknown path redirects to `/`.

## ESLint / Prettier

- ESLint flat config (`eslint.config.js`): typescript-eslint
  `recommendedTypeChecked` + `stylisticTypeChecked`, React Hooks (classic
  rules-of-hooks + exhaustive-deps), react-refresh, and jsx-a11y. Prettier
  config is applied last to disable formatting rules.
- Prettier (`.prettierrc.cjs`): `printWidth: 120`, single quotes, trailing
  commas, tailwind class sorting.

### Warnings to tighten over time

To establish tooling **green** without a risky refactor of existing code, several
type-checked and a11y rules are currently set to **`warn`** (lint still exits 0).
These are tech debt to be tightened incrementally as files are touched:

- `@typescript-eslint`: `no-floating-promises`, `no-misused-promises`,
  `no-explicit-any`, `no-unsafe-*`, `require-await`, `no-empty-function`,
  `no-unused-expressions`, `prefer-nullish-coalescing`,
  `consistent-type-imports`.
- `jsx-a11y`: `no-autofocus`, `click-events-have-key-events`,
  `no-static-element-interactions`, `no-noninteractive-element-interactions`.
- `react-refresh/only-export-components`, `react-hooks/exhaustive-deps`.

New code should aim to be clean against these (don't add new warnings).

## Pre-commit (husky + lint-staged)

A pre-commit hook (`web/.husky/pre-commit`) runs lint-staged (eslint --fix +
prettier --write) on staged `web/src` ts/tsx. Enable it locally with:

```bash
git config core.hooksPath web/.husky
```

CI is the authoritative safety net regardless of the local hook.
