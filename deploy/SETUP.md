# Deploy — DEV setup

The DEV deploy (`.github/workflows/deploy.yml`) builds the API + Web images,
pushes them to GHCR, and runs `docker stack deploy` of
[`docker-compose.swarm.yml`](./docker-compose.swarm.yml) on the shared
self-hosted `[self-hosted, ulbra]` runner. Routing/TLS is via the shared Traefik.

Everything below must be configured **outside the repo** — the workflow assumes
it already exists.

## 1. GitHub Environment `dev`

Create an Environment named **`dev`** (Settings → Environments) with:

### Variables (`vars`)
| Name | Example | Notes |
| --- | --- | --- |
| `API_HOST` | `one-api-dev.ulbra.br` | Public host for the API. Traefik routes `Host(API_HOST)` → api:8080. Also used to build `AllowedOrigins__0` and `VITE_API_BASE_URL=https://API_HOST/api`. |
| `WEB_HOST` | `one-dev.ulbra.br` | Public host for the SPA. Traefik routes `Host(WEB_HOST)` → web:80. |
| `ASPNETCORE_ENVIRONMENT` | `Staging` | **Decide deliberately — see below.** |

### Secrets (`secrets`)
| Name | Notes |
| --- | --- |
| `POSTGRES_USER` | DB user. Used in the container and to build the connection string. |
| `POSTGRES_PASSWORD` | DB password. |
| `JWT_SECRET` | **32+ chars.** Signs API JWTs (`Authentication__JwtSecret`). |
| `GOOGLE_CLIENT_ID_VALUE` | Google OAuth client id (`Authentication__Google__ClientId` + `VITE_GOOGLE_CLIENT_ID`). Google login is currently disabled in the UI, but the value is still wired through. |

> `GITHUB_TOKEN` (auto-provided) is used to push images to GHCR — no manual secret needed.

## 2. `ASPNETCORE_ENVIRONMENT` decision (important)

This flag changes API behavior. For a DEV environment, **`Staging`** is the
recommended default (production-like, but not the literal `Development` profile):

- **`Development`** → the **data seeder runs**, **rate-limiting is disabled**,
  human-readable (non-JSON) logging, and Swagger is exposed. Pick this only if
  you explicitly want seeded demo data and an open API on the DEV box.
- **`Staging` / `Production`** → no seeder, rate-limiting on, JSON logging,
  Swagger off. Closest to real behavior; safest default for a shared DEV host.

Recommendation: set `ASPNETCORE_ENVIRONMENT = Staging`. Switch to `Development`
only intentionally when you want the seeded dataset.

## 3. DNS

Create A/CNAME records pointing both hosts at the swarm ingress (same load
balancer/IP used by the SAU stack / Traefik):

- `API_HOST`  (e.g. `one-api-dev.ulbra.br`) → swarm ingress
- `WEB_HOST`  (e.g. `one-dev.ulbra.br`)     → swarm ingress

Traefik provisions Let's Encrypt certs automatically on first request once DNS
resolves.

## 4. Shared infrastructure (already provided by SAU)

These are shared on the same swarm and are **not** created by this repo:

- External overlay networks **`proxy`** (Traefik) and **`monitoring`**
  (OTel collector at `otel-collector:4317`).
- **Traefik** with the `letsencrypt` cert resolver and `websecure` entrypoint.
- The `[self-hosted, ulbra]` GitHub Actions runner.

The stack creates its own `internal` overlay network and `pgdata` volume.

## 5. Notes

- The API **auto-applies EF Core migrations on startup** (`Database.MigrateAsync()`),
  so no manual migration step is needed after deploy.
- Postgres data persists in the `pgdata` named volume (per-stack).
- The web image is environment-agnostic: `VITE_*` values are injected at
  container startup into `/usr/share/nginx/html/env.js` (see
  `web/docker-entrypoint.sh`), so the same image works in any environment.
- Deploy uses `--detach=false`, so the workflow waits for convergence and fails
  (with diagnostics) if a service doesn't stabilize.
