# Frontend Conventions — quick checklist

React 19 + Vite 6 + TypeScript (strict) in `web/`. Long-form: [`../web/AGENTS.md`](../web/AGENTS.md).
"do X / don't Y" cheatsheet; every pattern below exists in `web/src`.

## Generated API types (single source of truth)

- Wire types are **generated** from the backend OpenAPI spec via `openapi-typescript` →
  `web/src/api/schema.d.ts`. **Don't edit it by hand** (Prettier/ESLint ignore it).
- `web/openapi.json` is a committed copy of `/swagger/v1/swagger.json` so generation is reproducible.
- `api.ts` aliases generated schemas under stable names so pages/queries don't churn:

```ts
type Schemas = components['schemas'];
export type Company = Schemas['CompanyResponse'];
export type Payable = Schemas['PayableResponse'];
```

- Refresh after any backend contract change:

```bash
dotnet run --project src/API/Ulbra.One.Api          # or docker compose up -d
curl -s http://localhost:5001/swagger/v1/swagger.json -o web/openapi.json
cd web && pnpm gen:api && pnpm checks
```

## IDs are plain strings on the FE

- Typed prefixed IDs (`payable_…`) arrive as **plain `string`** in the generated types. Pass them
  straight through (`id: string`); no wrapper type, no parsing. Query-key params take `string`.

## API client + TanStack Query

- `api.ts` owns one `request<T>()` (sets `Content-Type`, `Authorization: Bearer`, 401 → clear token +
  redirect to `/login`, parses `problem+json` into `ApiError`). All calls go through `api.*`.
- `queries.ts` owns the hooks. **Query keys live in one `queryKeys` factory** — never inline a key array:

```ts
export const queryKeys = {
  payables: (params: PayablesParams) => ['payables', params] as const,
  payable: (id: string) => ['payable', id] as const,
};
```

- Mutations use the `useInvalidatingMutation` wrapper, which owns cache invalidation in `onSuccess`
  while still forwarding the caller's `onSuccess`. Invalidate the relevant `queryKeys.*` (or a prefix
  like `['vendors']`) after a write — don't refetch manually.

## Forms — react-hook-form + zod

- `useForm<T>({ resolver: resolver(schema), mode: 'onChange' })`; schemas live in `pages/schemas.ts`
  (`z.object(...)`, `@hookform/resolvers/zod`). Field rules **mirror the backend validators**.
- Validation messages are **i18n keys**, resolved lazily so the active language wins
  (`message: 'validation.emailInvalid'`, `t = (key) => i18n.t(key)`).

## UI primitives + icons

- Radix primitives are wrapped in `src/ui.tsx` (the only `@radix-ui` consumer) — import shared UI from
  `./ui`, don't pull Radix into pages directly. Icons: `lucide-react`.

## i18n — English keys, pt-BR values

- `react-i18next`; bundles in `src/i18n/locales/`, default + fallback = `pt-BR.ts`. **Keys are English**,
  pt-BR is the source-of-truth shape; a new language = a sibling file with the same keys.
- In components: `const { t } = useTranslation();` then `t('nav.accountsPayable')`.
- **NEVER hardcode pt-BR in JSX** — new screens must route all text through `t(...)`.
- Backend error codes map to text via `errors.codes.<Aggregate.Reason>`; `getErrorMessage(e)` in
  `apiError.ts` resolves an `ApiError.title` to that key, falling back to `detail`/field errors.

## Authed file download (fetch → blob)

- A plain `<a href>` can't send the Bearer token, so downloads fetch with the `Authorization` header,
  turn the response into an object-URL blob, and click a synthetic `<a download>`:

```ts
const res = await fetch(url, { headers });           // headers carries Bearer auth.token
const objectUrl = URL.createObjectURL(await res.blob());
const a = document.createElement('a');
a.href = objectUrl; a.download = fileName; a.click(); a.remove();
URL.revokeObjectURL(objectUrl);
```

- Multipart uploads also use raw `fetch` (let the browser set the boundary), not the JSON `request()`.

## Formatting helpers

- Money: `brl(v)` / `brlCompact(v)` in `src/lib.ts` (`Intl` `pt-BR`/`BRL`).
- Document/phone/CEP masks, `isValidCpf`/`isValidCnpj`, and `fmtDoc` (CPF/CNPJ display by
  length): `src/format.ts`.
- Dates: import from `src/datetime.ts` — `fmtDate(iso)` (DateOnly → pt-BR),
  `fmtDateTime(iso)` (ISO date-time → pt-BR date), `toDateOnly(d)` (local `Date` →
  `'YYYY-MM-DD'`; never `toISOString()`, UTC-3 shifts the day). Don't redefine these per
  page — import the shared helper.

## Report headers — stacked title + filter row

- Report screens (Contábil / relatórios: DRE, Balancete, Livro Diário, Razão) use the shared **`ReportHead`**
  (`pages/contabil-shared.tsx`): the page **title + subtitle stacked in their own block**, and the filter
  fields on a row **below**. Do **not** cram title and filters onto one line (`PageHeader` toolbar) — there is
  too much info for one row, so the title/subtitle wrap and the header looks broken across widths.
- Filters are passed as `ReportHead` children (a fragment of fields, laid out in `.razao-filters-row`): use
  the shared **`ReportToolbar`** (competence range + unidade + centro de custo + optional CSV export) for
  DRE/Balancete; screens with a different filter set (Livro Diário, Razão) pass their own fragment.
- **Never add an empresa/consolidado filter to a page.** The company scope has exactly one control — the
  selector in the topbar (`EmpresaSelector` → `useApp`). Read it as `scopeCompanyId` (a real id, or
  `undefined` in the consolidated "Todas as empresas" view) and gate the query on `scopeReady`. A second
  company field on the page duplicates the topbar and lets the two disagree. Company-scoped sub-filters
  (unidade, centro de custo) don't need manual clearing on a company change: `KeyedOutlet` (App.tsx)
  remounts the routed page.
- CSV export goes through `ReportToolbar`'s `onExport` (button pinned right via `.razao-export`); the download
  itself uses `downloadWithAuth(api.<report>ExportUrl(...))` (Bearer-authed blob — see "Authed file download").
- **Verify a new report header:** `pnpm --dir web dev` + `pnpm --dir web shots` and compare consolidated vs
  empresa-selected (the pr-shots script mocks the API per screen).

## Before pushing

```bash
cd web && pnpm checks   # format:check + lint + typecheck
```
