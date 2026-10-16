# Ulbra.One

ERP da ULBRA — .NET 10 modular monolith (single deployable, multiple bounded contexts).
Authoritative project instructions live in [`CLAUDE.md`](CLAUDE.md); this file is the guide index for agent tools.

## Detailed guides
- [`docs/backend-conventions.md`](docs/backend-conventions.md) — API design + patterns (endpoints, Result, persistence, auth)
- [`docs/api-design.md`](docs/api-design.md) — REST/endpoint contract conventions
- [`docs/frontend-conventions.md`](docs/frontend-conventions.md) — web app conventions (i18n, UX)
- [`docs/testing.md`](docs/testing.md) — test suites, fixtures, how to run
- [`docs/runbooks/new-module.md`](docs/runbooks/new-module.md) — adding a new bounded-context module
- [`docs/runbooks/migrations.md`](docs/runbooks/migrations.md) — EF Core migrations workflow
- [`docs/runbooks/azure-deploy.md`](docs/runbooks/azure-deploy.md) — Azure bootstrap, GitHub secrets, deploy pipeline, portable config matrix, scaling, rollback

Every module has its own `AGENTS.md` (mirrored as `CLAUDE.md`) under `src/Modules/<Module>/` — read it before working in that module: `Core`, `Finance`, `Registry`, `Identity`, `Notifications`.
