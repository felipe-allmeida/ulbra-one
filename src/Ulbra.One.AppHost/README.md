# Ulbra.One.AppHost — Local Dev Orchestration

**.NET Aspire 9.5.2** AppHost that spins up the full local stack with a single command.

## Prerequisites

- Docker (Desktop or Engine) — running containers: Postgres, RabbitMQ, Azurite
- .NET 10 SDK (no workload install needed — Aspire 9 uses the `Aspire.AppHost.Sdk` MSBuild SDK, not a workload)
- Node.js + pnpm (for the Vite web app)

## Run

```bash
dotnet run --project src/Ulbra.One.AppHost
```

This opens the **.NET Aspire Dashboard** (http://localhost:15888 by default) and starts:

| Resource    | What                                          |
|-------------|-----------------------------------------------|
| `postgres`  | PostgreSQL container (migrations auto-applied) |
| `rabbitmq`  | RabbitMQ container (management UI on :15672)   |
| `storage`   | Azurite emulator (Azure Blob Storage)          |
| `api`       | `Ulbra.One.Api` (hot-reload via Aspire)        |
| `web`       | Vite dev server (`pnpm dev`)                   |

## Config Key Mapping

Aspire injects environment variables into the `api` process:

| App key                        | Source                                                          |
|--------------------------------|-----------------------------------------------------------------|
| `ConnectionStrings__Database`  | Aspire Postgres integration (database resource named "Database") |
| `MassTransit__Transport`       | Hardcoded `RabbitMq`                                            |
| `MassTransit__Host`            | RabbitMQ container hostname                                     |
| `MassTransit__Port`            | RabbitMQ container port (5672)                                  |
| `MassTransit__User`            | RabbitMQ username parameter                                     |
| `MassTransit__Password`        | RabbitMQ password parameter                                     |
| `ObjectStorage__Provider`      | Hardcoded `AzureBlob`                                           |
| `ObjectStorage__Bucket`        | Hardcoded `attachments`                                         |
| `ObjectStorage__ConnectionString` | Azurite blob connection string (from `blobs.Resource.ConnectionStringExpression`) |

The web app receives `PORT` (HTTP port) and `VITE_API_BASE_URL` pointing at the running API.

## Upload flow (fiscal / SAS)

The fiscal-document upload is *presigned*: the browser PUTs the file directly to a SAS URL returned
by the API. Under Aspire the API runs as a host process and Azurite is exposed on `127.0.0.1:<port>`,
so the SAS URL host is already reachable from the browser. In `docker-compose` the server reaches
Azurite at the in-network `azurite:10000`, which the browser can't resolve — there
`ObjectStorage:PublicBlobEndpoint` (`http://localhost:10000`) rewrites the SAS URL host so the
browser PUT works. Either way the full upload flow (incl. fiscal/SAS) works locally.

## Notes

- This project is **dev-only**. It is not referenced by CI, tests, or any deploy pipeline.
- Tests continue to use Testcontainers directly (unaffected by this AppHost).
- Authentication secrets (JwtSecret, Google ClientId) must still be set in `appsettings.Development.json` or user secrets — they are not managed by Aspire.
