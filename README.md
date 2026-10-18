# ULBRA One

ERP interno da ULBRA — plataforma própria, modular e integrada, para substituir os sistemas legados (Senior etc.).

Stack: **.NET 10** (modular monolith) · **PostgreSQL 17** · EF Core (code-first, snake_case) · React + Tailwind + shadcn (frontend, a vir).

> Convenções de arquitetura espelham o `ulbra-sau` (modular monolith, `IEndpoint`, `Result<T>`, migrate-on-startup). Veja `CLAUDE.md`.

## Subir local

```bash
docker compose up -d --build       # Postgres + API + Web
curl http://localhost:5001/health  # deve responder Healthy
open http://localhost:5001/swagger # API docs
open http://localhost:5173         # frontend (login "Entrar (dev)")
```

Sem Docker pro backend/front:

```bash
docker compose up -d postgres
dotnet run --project src/API/Ulbra.One.Api      # API em :5001
cd web && pnpm install && pnpm dev              # web em :5173
```

## Estrutura

```
src/
  API/Ulbra.One.Api/            Entry point (Program.cs wires modules)
  Common/                       Domain (Result/Error) · Application (IEndpoint) · Infrastructure
  Modules/
    Core/                       Empresa (raiz do organograma) — primeiro módulo
```

Cada módulo segue **Domain → Application → Infrastructure**.

## Migrations

```bash
dotnet ef migrations add <Nome> \
  --project src/Modules/Core/Ulbra.One.Core.Infrastructure \
  --startup-project src/API/Ulbra.One.Api \
  --context CoreContext \
  --output-dir Database/Migrations
```

Aplicadas automaticamente no startup da API (`Database.MigrateAsync()`).
