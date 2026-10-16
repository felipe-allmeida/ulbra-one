# Testing — rules + commands

**Tests for everything, no exceptions.** A feature/bugfix without tests is not done. Write tests
TDD-style where practical (red → green → refactor). Every pattern below exists in the repo.

## Backend — xUnit, two suites

`net10.0`, nullable + `TreatWarningsAsErrors` (`Directory.Build.props`).

- **`tests/Ulbra.One.UnitTests`** — fast, **no Docker**. Pure logic only: domain entities/aggregates,
  validators, `ResultExtensions`, `AuditInterceptor`, `DocumentValidator`, normalization, IDs, storage.
- **`tests/Ulbra.One.IntegrationTests`** — real Postgres via **Testcontainers** (`PostgresFixture`), plus a
  real Redis via Testcontainers (`RedisFixture`) for the realtime backplane tests, +
  `ApiFactory : WebApplicationFactory<Program>` (forced to the **Development** environment so
  `/auth/dev-token` + migrations work). Hit real HTTP, real DB — no mocked persistence.

```csharp
// authenticated client via the dev-token endpoint
var client = await factory.CreateAdminClientAsync();
var res = await client.PostAsJsonAsync("/api/payables", request);
```

- **Do**: unit-test domain invariants directly on the aggregate; integration-test endpoints end-to-end
  (validation filter, Result→Problem mapping, snake_case persistence, cross-module reads).
- **Don't**: spin up Testcontainers for logic that's pure (keep it in UnitTests — it must stay Docker-free).

Run:

```bash
dotnet test                                              # everything
dotnet test tests/Ulbra.One.UnitTests                    # fast, no Docker
dotnet test tests/Ulbra.One.IntegrationTests             # needs a running Docker daemon
```

## Frontend — Vitest + Testing Library

- jsdom, colocated `*.test.ts(x)` next to source. Used for utils (`format`, `apiError`, `useAsync`) and
  components (`ui`, `combobox`).

```bash
cd web
pnpm test            # run once
pnpm test:watch      # watch mode
```

## E2E — Playwright (smoke)

- One resilient smoke flow in `web/e2e/smoke.spec.ts` (landing → Entrar → login → dev-token →
  `/app/inicio` → Fornecedores renders) + a logged-out redirect check. It does **not** assert business data.
- `playwright.config.ts`: **`workers: 1`, `fullyParallel: false`** — serial against one shared, seeded DB.
- **Does NOT start the backend** — bring up the stack first; Playwright only starts/reuses the Vite dev
  server on **:5173** (required: the dev API only allows CORS origin `http://localhost:5173`).

```bash
docker compose up -d                          # backend + postgres + seed (or: dotnet run --project src/API/Ulbra.One.Api)
curl -s http://localhost:5001/api/ping        # sanity check
cd web
pnpm exec playwright install chromium         # first time only
pnpm test:e2e                                 # or pnpm test:e2e:ui
```

## CI

- `.github/workflows/api-ci.yml` — restore → build (Release) → unit tests → integration tests
  (Testcontainers on the runner's Docker), on changes to `src/`, `tests/`, props, or the solution.
- `.github/workflows/web-ci.yml` — typecheck + lint + vitest + build on `web/**`.
- Full-stack E2E is **not** wired into CI yet (run locally). The husky pre-commit hook only lints the
  frontend — backend quality relies on CI.
