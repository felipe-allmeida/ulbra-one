# Ulbra.One

ERP da ULBRA. .NET 10 modular monolith — single deployable, múltiplos bounded contexts.

> **Guias detalhados** (índice em [`AGENTS.md`](AGENTS.md)): [`docs/backend-conventions.md`](docs/backend-conventions.md) · [`docs/api-design.md`](docs/api-design.md) · [`docs/frontend-conventions.md`](docs/frontend-conventions.md) · [`docs/testing.md`](docs/testing.md) · runbooks [`new-module`](docs/runbooks/new-module.md) / [`migrations`](docs/runbooks/migrations.md). Cada módulo tem seu `AGENTS.md` em `src/Modules/<Module>/`.

## Build & Run
```bash
dotnet build
dotnet run --project src/API/Ulbra.One.Api
docker compose up -d --build
```
- **Aspire local dev** (requires Docker): `dotnet run --project src/Ulbra.One.AppHost` — starts API + all dependencies via .NET Aspire.
- **Azure target**: Terraform + Container Apps — see [`docs/runbooks/azure-deploy.md`](docs/runbooks/azure-deploy.md) for the full bootstrap + deploy runbook.

## Architecture
```
src/
  API/Ulbra.One.Api/   Entry point (Program.cs wires everything)
  Common/              Domain, Application, Infrastructure compartilhados
  Modules/
    Core/              Empresa (raiz do organograma) — primeiro módulo
    Finance/           Payable (contas a pagar)
    Registry/          Vendor, Customer, Employee
    Identity/          Usuários + auth (JWT / Google OAuth)
    Notifications/     In-app notifications (audience-by-role)
```
Cada módulo segue **Domain → Application → Infrastructure**.

## Key Conventions
- **Database**: EF Core code-first com `UseSnakeCaseNamingConvention()` — colunas em snake_case vêm da convenção. **Single `AppDbContext`** em `Ulbra.One.Persistence`; cada módulo possui suas entidades + suas configurações herdando `BaseEntityConfiguration<T>`, que declaram schema (`Schema`) e **nome da tabela** (`TableName`, já em snake_case, ex.: `"payables"`). O nome da tabela é declaração explícita da config — não emerge do nome da propriedade `DbSet`. Audit é exceção deliberada: possui `AuditContext` próprio + UoW keyed `"audit"` (write separado).
- **Model por módulo**: o `AppDbContext` não referencia módulo nenhum — ele itera `IEnumerable<IModuleModelConfiguration>` vindo do DI. Cada módulo registra sua contribuição no próprio `AddXModule` com `services.AddModelConfiguration(typeof(<X>Module).Assembly)`; módulos com mapeamento fora do padrão (Audit) implementam `IModuleModelConfiguration` e registram com `AddModelConfiguration<T>()`. Ao criar um módulo, adicione também a assembly em `ModuleModelConfigurations.All` (`src/API/Ulbra.One.Api/Database/`) — é a lista lida em design-time pelas ferramentas do EF. Não há `DbSet<>`: o acesso a entidades é `Set<T>()`, normalmente via `IUnitOfWork`.
- **Cross-module refs**: módulos referenciam outros apenas por `Id` solto — nunca foreign keys entre módulos (como se fossem bancos separados). Preserva modularidade mesmo com um único contexto físico.
- **`IUnitOfWork`**: um único, sem key (`inject IUnitOfWork` diretamente, sem `[FromKeyedServices]`). Audit é a única exceção keyed.
- **Migrations**: em `Ulbra.One.Persistence/Migrations/`, geradas com `dotnet ef migrations add <Name> --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext`; auto-aplicadas no startup via `Database.MigrateAsync()`.
- **Endpoints**: padrão `IEndpoint` (minimal API), mapeados sob `/api`. Sem controllers.
- **Error handling**: `Result<T>` / `Error` railway pattern. Todo erro sai por `ErrorResults` (`Common.Application/Errors/`) — envelope RFC 9457 com `trace_id` + `error_code`; os códigos vivem em `ErrorCodes` (`Common.Domain/Errors/`) e cada um exige a tradução pt-BR correspondente (guarda em `ErrorCatalogueTests`). Veja `docs/api-design.md`.
- **Central packages**: versões em `Directory.Packages.props`.
- **Realtime**: subsistema transversal em `Common.Application/Realtime` + `Common.Infrastructure/Realtime` — SSE (`GET /api/realtime/stream`), presença por recurso, e **Redis como backplane entre réplicas** (dependência de infra sempre ligada; `Redis:ConnectionString`). Sem Redis configurado cai para um backplane in-memory que NÃO cruza réplicas — o health check `realtime-backplane` reporta degradado (nunca derruba o readiness). Ponto de extensão: `IPresenceAuthorizer` (um por escopo de recurso; ver `RequisitionPresenceAuthorizer` em Budgeting). Design em `docs/superpowers/specs/2026-08-28-realtime-updates-design.md`.
- **Typed IDs**: declare como `[PrefixedId("<prefix>_")] public readonly partial record struct XId(Guid Value);` — o source generator (`Ulbra.One.SourceGenerators`) emite `IPrefixedId`, `IParsable<T>`, `JsonConverter`, `Prefix`, `New()`, `ToString()`, `Parse`/`TryParse`, e `PrefixedIdRegistry.All` (consumido em `Program.cs` para registrar os schemas OpenAPI; não é necessário `MapType` por id).
- **Eventing / Outbox**: MassTransit v8 + RabbitMQ com **EF bus outbox** no `AppDbContext` (`masstransit` schema — `inbox_state`, `outbox_message`, `outbox_state`). Num **handler de endpoint**, publique via `IPublishEndpoint` **antes** de `SaveChanges` — mensagem e linha de negócio commitam atomicamente. Num **consumer** com outbox de consumer (`UseEntityFrameworkOutbox`, ver `MessagingSetup.OutboxEnrolledEndpoints`) a ordem não vincula: o `Publish` estagia a linha no `DbContext` do escopo e o outbox a commita, então publicar **depois** do `SaveChanges` é igualmente atômico — é o que os cinco consumers de Notifications fazem. **Um consumer que publica e não está em `OutboxEnrolledEndpoints` perde a mensagem em silêncio** (a linha estagiada é descartada no dispose do escopo — sem exceção, sem log). Foundation completa (AEL-588) e em uso: consumers registrados em `MessagingSetup.cs` (Finance `PayableReceivedConsumer`/`PayableUpdatedConsumer`; Accounting `PayableScheduledConsumer`/`PaymentRegisteredConsumer`/`AccountingReversalConsumer`). Consumers implementam `IConsumer<T>` e devem ser idempotentes (at-least-once). Veja `docs/backend-conventions.md` §12 para o guia completo.

## Anti-Patterns (do NOT)
- Adicionar controllers — use `IEndpoint`.
- `.HasColumnName()` manual — a convenção snake_case resolve os nomes de coluna.
- `.ToTable()` avulso dentro de `ConfigureEntity` — o nome da tabela se declara na propriedade `TableName` da config (a base o aplica junto com o schema).
- Escrever migrations à mão — sempre `dotnet ef migrations add`.
- Foreign keys entre módulos — referencie por `Id` solto.
- Referenciar projeto de módulo a partir de `Ulbra.One.Persistence` — use o seam `IModuleModelConfiguration` (modelo) ou `IInterceptor` (comportamento).
- Montar `ProblemDetails` à mão (`TypedResults.Problem`, `Results.Problem`) — use as factories de `ErrorResults`.
- Código de erro como literal no call site — declare em `ErrorCodes`.
- Lógica de negócio em endpoint — mantenha no domínio.
- Comentar o óbvio — o padrão é código que se lê sem comentário. Comente só o que o código não consegue dizer (um *porquê* não óbvio, uma restrição externa, um número medido). Detalhe de mudança vai no commit/PR, não no arquivo. Ver `docs/backend-conventions.md` §9.

## Branch & PR
- Branch: `AEL-<n>/descricao-kebab`
- PR title: `tipo(AEL-<n>): descrição` (`feat`, `fix`, `chore`, `ci`, `refactor`, `docs`, `test`)
