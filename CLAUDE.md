# Ulbra.One

ERP da ULBRA. .NET 10 modular monolith — single deployable, múltiplos bounded contexts.

> **Guias detalhados** (índice em [`AGENTS.md`](AGENTS.md)): [`docs/backend-conventions.md`](docs/backend-conventions.md) · [`docs/api-design.md`](docs/api-design.md) · [`docs/frontend-conventions.md`](docs/frontend-conventions.md) · [`docs/testing.md`](docs/testing.md) · runbooks [`new-module`](docs/runbooks/new-module.md) / [`migrations`](docs/runbooks/migrations.md). Cada módulo tem seu `AGENTS.md` em `src/Modules/<Module>/`.

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
- **Database**: EF Core code-first com `UseSnakeCaseNamingConvention()` — tabelas/colunas snake_case. **Single `AppDbContext`** em `Ulbra.One.Persistence`; cada módulo possui suas entidades + classes `IEntityTypeConfiguration<T>` e define o schema via `e.Metadata.SetSchema("<schema>")` (única exceção permitida ao anti-pattern de `.ToTable()` — apenas schema, nunca nome de tabela/coluna). Audit é exceção deliberada: possui `AuditContext` próprio + UoW keyed `"audit"` (write separado).
- **Cross-module refs**: módulos referenciam outros apenas por `Id` solto — nunca foreign keys entre módulos (como se fossem bancos separados). Preserva modularidade mesmo com um único contexto físico.
- **`IUnitOfWork`**: um único, sem key (`inject IUnitOfWork` diretamente, sem `[FromKeyedServices]`). Audit é a única exceção keyed.
- **Migrations**: em `Ulbra.One.Persistence/Migrations/`, geradas com `dotnet ef migrations add <Name> --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext`; auto-aplicadas no startup via `Database.MigrateAsync()`.
- **Endpoints**: padrão `IEndpoint` (minimal API), mapeados sob `/api`. Sem controllers.
- **Error handling**: `Result<T>` / `Error` railway pattern.
- **Central packages**: versões em `Directory.Packages.props`.

## Anti-Patterns (do NOT)
- Adicionar controllers — use `IEndpoint`.
- `.ToTable()` / `.HasColumnName()` manual — a convenção snake_case resolve. (`.SetSchema()` via `e.Metadata.SetSchema("<schema>")` é a única exceção permitida — apenas para definir o schema do módulo.)
- Escrever migrations à mão — sempre `dotnet ef migrations add`.
- Foreign keys entre módulos — referencie por `Id` solto.
- Lógica de negócio em endpoint — mantenha no domínio.

## Branch & PR
- Branch: `AEL-<n>/descricao-kebab`
- PR title: `tipo(AEL-<n>): descrição` (`feat`, `fix`, `chore`, `ci`, `refactor`, `docs`, `test`)
