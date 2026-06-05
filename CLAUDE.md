# Ulbra.One

ERP da ULBRA. .NET 10 modular monolith — single deployable, múltiplos bounded contexts.

> Full backend conventions (API design + patterns): [`docs/backend-conventions.md`](docs/backend-conventions.md).

## Build & Run
```bash
dotnet build
dotnet run --project src/API/Ulbra.One.Api
docker compose up -d --build
```

## Architecture
```
src/
  API/Ulbra.One.Api/   Entry point (Program.cs wires everything)
  Common/              Domain, Application, Infrastructure compartilhados
  Modules/
    Core/              Empresa (raiz do organograma) — primeiro módulo
```
Cada módulo segue **Domain → Application → Infrastructure**.

## Key Conventions
- **Database**: EF Core code-first com `UseSnakeCaseNamingConvention()` — tabelas/colunas snake_case. Schema por módulo (`core`).
- **Migrations**: por módulo em `Infrastructure/Database/Migrations/`, auto-aplicadas no startup via `Database.MigrateAsync()`.
- **Endpoints**: padrão `IEndpoint` (minimal API), mapeados sob `/api`. Sem controllers.
- **Error handling**: `Result<T>` / `Error` railway pattern.
- **Central packages**: versões em `Directory.Packages.props`.

## Anti-Patterns (do NOT)
- Adicionar controllers — use `IEndpoint`.
- `.ToTable()` / `.HasColumnName()` manual — a convenção snake_case resolve.
- Escrever migrations à mão — sempre `dotnet ef migrations add`.
- Lógica de negócio em endpoint — mantenha no domínio.

## Branch & PR
- Branch: `AEL-<n>/descricao-kebab`
- PR title: `tipo(AEL-<n>): descrição` (`feat`, `fix`, `chore`, `ci`, `refactor`, `docs`, `test`)
