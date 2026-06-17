# Backend Conventions — Ulbra.One API

Conventions for the .NET 10 modular-monolith API (`src/`). This reflects the **current** code,
not an aspiration. When in doubt, the code wins — fix the doc, don't invent.

See also the root `CLAUDE.md` for the short version and anti-patterns.

---

## 1. Architecture

Modular monolith: one deployable (`Ulbra.One.Api`), multiple bounded contexts. Each module is split
into three projects following **Domain → Application → Infrastructure**.

| Module | Aggregates / entities | DB schema | Projects |
|---|---|---|---|
| `Core` | Company, Unit, CostCenter (+ CostCenterUnit) | `core` | `Ulbra.One.Core.{Domain,Application,Infrastructure}` |
| `Finance` | Transaction | `finance` | `Ulbra.One.Finance.{Domain,Application,Infrastructure}` |
| `Identity` | User / auth | `identity` | `Ulbra.One.Identity.{Domain,Application,Infrastructure}` |
| `Registry` | VendorEntity, CustomerEntity, EmployeeEntity (Address VO) | `registry` | `Ulbra.One.Registry.{Domain,Application,Infrastructure}` |
| `Common` | shared primitives (Result/Error, pagination, auditing, auth, validation, observability) | — | `Ulbra.One.Common.{Domain,Application,Infrastructure}` |

Layer dependencies: **Domain** has no infra deps; **Application** holds endpoints, DTOs (Contracts),
validators; **Infrastructure** holds the `DbContext`, migrations, DI module, external lookups.

**DI wiring** is per-module and assembled in `src/API/Ulbra.One.Api/Program.cs`:

```csharp
// Services
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddCoreModule(builder.Configuration);
builder.Services.AddFinanceModule(builder.Configuration);
builder.Services.AddRegistryModule(builder.Configuration);
builder.Services.AddRegistryValidators();
// ...
// Routes
var apiGroup = app.MapGroup("/api");
apiGroup.MapIdentityEndpoints();
apiGroup.MapCoreEndpoints();
apiGroup.MapFinanceEndpoints();
apiGroup.MapRegistryEndpoints();
```

Each module exposes `AddXModule(IServiceCollection, IConfiguration)` (registers its services) and
`MapXEndpoints(IEndpointRouteBuilder)` (instantiates each `IEndpoint`). New module →
add both calls to `Program.cs`.

**Persistence stack**: EF Core 10 code-first on Npgsql with `UseSnakeCaseNamingConvention()`
(tables/columns become snake_case automatically). There is a **single `AppDbContext`** in
`Ulbra.One.Persistence`; modules do NOT each own a `DbContext`. Instead each module owns its
entity classes + `IEntityTypeConfiguration<T>` classes (registered by calling
`modelBuilder.ApplyConfigurationsFromAssembly(...)` per module assembly). Schema is set per-entity
via `e.Metadata.SetSchema("<schema>")` inside the configuration — this is the **one allowed
`.ToTable`-adjacent exception** (schema only; table/column names are still derived by the
snake_case convention). Example:

```csharp
// CoreCompanyConfiguration.cs (inside Ulbra.One.Core.Infrastructure)
public void Configure(EntityTypeBuilder<Company> e)
{
    e.Metadata.SetSchema("core");
    // indexes, owned types, etc. — no .ToTable(), no .HasColumnName()
}
```

**Cross-module references**: even though all modules share one DB and one context, modules must
**never** create foreign key constraints to another module's tables. Reference other modules only
by loose `Id` (e.g. `Guid VendorId` on `Payable` — no navigation property, no FK). This preserves
bounded-context modularity now that the physical context boundary is gone.

**Audit exception**: `AuditContext` (in `Ulbra.One.Audit.Infrastructure`) is the only separate
context — it has its own dedicated keyed `"audit"` `IUnitOfWork` for append-only audit writes.

Migrations live in `Ulbra.One.Persistence/Migrations/` and are **auto-applied at startup** in
`Program.cs` (with a retry loop, since Postgres may not be ready when the API boots):

```csharp
await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
```

Generate new migrations with:
```bash
dotnet ef migrations add <Name> \
  --project src/Ulbra.One.Persistence \
  --startup-project src/API/Ulbra.One.Api \
  --context AppDbContext
```

---

## 2. Endpoints

Minimal APIs only — **no controllers**. Each feature is a `sealed class : IEndpoint` with a single
`Map(IEndpointRouteBuilder)`:

```csharp
public sealed class VendorsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/vendors").WithTags("Registry").RequireAuthorization();
        // group.MapGet/MapPost/...
    }
}
```

Conventions:

- **Route group per resource**, kebab-case English paths: `/companies`, `/units`,
  `/cost-centers`, `/transactions`, `/vendors`, `/customers`, `/employees`, `/lookup`, `/auth`.
  Everything is mounted under `/api` by `Program.cs`.
- `.WithTags("…")` groups operations in Swagger (`Core`, `Finance`, `Registry`, `Auth`).
- `.WithName("…")` per operation (stable operationId for the generated TS client).
- **All data endpoints `.RequireAuthorization()`** at the group level. `/auth` is the only public
  group. Admin-only routes use `.RequireAdmin()` (see §7) — available, currently no route uses it.
- Handlers map the **entity → response DTO** (a `record` in `Application/Contracts/`) via a static
  `From(entity)` factory — never return entities or anonymous objects.
- Annotate the OpenAPI contract: `.Produces<TDto>(StatusCodes.Status200OK)`,
  `.Produces<TDto>(StatusCodes.Status201Created)`, `.ProducesProblem(StatusCodes.Status404NotFound)`, etc.

```csharp
group.MapGet("/{id:guid}", async (Guid id, RegistryContext db, CancellationToken ct) =>
{
    var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
    return (vendor is null
        ? Result.Failure<VendorResponse>(Error.NotFound("Vendor.NotFound", "Vendor not found."))
        : Result.Success(VendorResponse.From(vendor))).ToOk();
})
    .WithName("GetVendor")
    .Produces<VendorResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status404NotFound);
```

DTO members are emitted as required, enums as strings (global `JsonStringEnumConverter`); Swagger
schema filters in `Program.cs` (`RequiredPropertiesSchemaFilter`, `StringEnumSchemaFilter`) keep the
generated TS client's fields non-optional.

---

## 3. Errors & Result

Domain logic uses the **`Result` / `Result<T>` railway** (`Ulbra.One.Common.Domain`) with a typed
`Error(Code, Description, ErrorType)`. `ResultExtensions` turn a `Result` into an `IResult` and map
`ErrorType → HTTP status`, emitting **RFC 7807 Problem Details**:

```csharp
public static IResult ToOk<T>(this Result<T> result) =>
    result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error);

public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
    result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : Problem(result.Error);

private static IResult Problem(Error error) => TypedResults.Problem(
    detail: error.Description,         // English, human message
    statusCode: ToStatusCode(error.Type),
    title: error.Code);               // stable code, e.g. "Vendor.NotFound"
```

`ErrorType → status`: `Validation→400`, `NotFound→404`, `Conflict→409`, `Unauthorized→401`,
`Forbidden→403`; `Failure`/`Problem` deliberately fall through to **500** (they signal internal
conditions, not client errors).

**Error code contract** (the important bit):

- Problem `title` = a **stable `Domain.Reason` code** in English: `Vendor.NotFound`,
  `Transaction.AmountInvalid`, `Transaction.CostCenterInvalid`, `Auth.GoogleTokenInvalid`, …
- Problem `detail` = an English human message.
- The **frontend maps the code → pt-BR** via i18n (`errors.codes.*`). Do **not** localize on the
  backend — code in English, the code is the contract.

For simple endpoints, raw `TypedResults.Problem(...)` with an explicit `title:` code is fine (no `Result`
wrapper required):

```csharp
if (req.Amount <= 0)
    return TypedResults.Problem(detail: "Amount must be greater than zero.",
        statusCode: StatusCodes.Status400BadRequest, title: "Transaction.AmountInvalid");
```

Unhandled exceptions are caught by `UseAppExceptionHandler()` → 500 Problem Details (no stack trace);
`UseStatusCodePages()` emits `problem+json` for bare status codes like 404. `AddAppProblemDetails()`
sets `Instance` to the request path.

---

## 4. Validation

FluentValidation, applied through the generic endpoint filter — never inline in the handler:

```csharp
group.MapPost("/", async (VendorRequest req, RegistryContext db, CancellationToken ct) => { /* ... */ })
    .WithValidation<VendorRequest>()   // → 400 ValidationProblem with per-field errors
    .WithName("CreateVendor");
```

`ValidationEndpointFilter<TRequest>` resolves `IValidator<TRequest>`, validates the request arg, and
returns `TypedResults.ValidationProblem(errors)` grouped by property when invalid. Validators are
auto-registered per module (`AddValidatorsFromAssemblyContaining<…>` in `AddRegistryValidators`).

Validators set a stable **error code** alongside the message; CPF/CNPJ check-digit (módulo 11)
validation lives in `Common` (`DocumentValidator.IsValidCpf/IsValidCnpj`):

```csharp
RuleFor(x => x.Document)
    .Must((req, doc) => req.PersonType == PersonType.Company
        ? DocumentValidator.IsValidCnpj(doc) : DocumentValidator.IsValidCpf(doc))
    .WithMessage("Invalid document (CPF/CNPJ).").WithErrorCode("Document.Invalid");
```

---

## 5. Persistence

- **Single `AppDbContext`** (`Ulbra.One.Persistence`) — there are no per-module `DbContext` classes.
  Each module owns its entity classes + `IEntityTypeConfiguration<T>` (living in the module's
  `Infrastructure` project). `AppDbContext` discovers them by calling
  `ApplyConfigurationsFromAssembly(...)` for each module assembly.
- **Schema per entity** via `e.Metadata.SetSchema("<schema>")` inside the module's configuration
  class. This is the only `.ToTable`-adjacent exception permitted — it sets the DB schema only; it
  does **not** override table or column names (those remain snake_case from the convention).
- **No cross-module FKs** — reference other modules by loose `Id` only (no navigation properties,
  no FK constraints across schemas). Treat module boundaries as if they were separate databases.
- **One non-keyed `IUnitOfWork`** — inject `IUnitOfWork` directly (no `[FromKeyedServices]`).
  `Audit` is the deliberate exception: it has its own `AuditContext` + keyed `"audit"` `IUnitOfWork`.
- **No `.ToTable()` / `.HasColumnName()`** — the snake_case convention handles naming. Configure
  only what the convention can't infer (lengths, precision, indexes, owned types, query filters).
- **Auditing** — entities implement `IAuditable` (`CreatedAt/By`, `UpdatedAt/By`). `AuditInterceptor`
  (registered on the context) fills these on `SaveChanges` using `UserContext.UserId` (falls back to
  `"system"` outside a request) and **`TimeProvider`** (never `DateTime.UtcNow`):

  ```csharp
  foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
  {
      if (entry.State == EntityState.Added) ApplyCreated(entry.Entity, userId, timeProvider);
      else if (entry.State == EntityState.Modified) ApplyUpdated(entry.Entity, userId, timeProvider);
  }
  ```

- **Soft-delete** — entities implement `ISoftDeletable` (`DeletedAt`, `MarkDeleted(at)`). The context
  applies a **global query filter** `HasQueryFilter(x => x.DeletedAt == null)` so deleted rows are
  invisible. Handlers call `entity.MarkDeleted(timeProvider.GetUtcNow())` instead of physical delete.
- **Unique indexes** are partial so a document can be re-registered after a soft-delete:

  ```csharp
  e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique().HasFilter("deleted_at IS NULL");
  ```

- **Decimal precision** is explicit: `e.Property(x => x.CreditLimit).HasPrecision(18, 2)`.
- **Owned value objects** (e.g. `Address`) via `e.OwnsOne(x => x.Address, ConfigureAddress)`.
- **Migrations**: always generate via `dotnet ef migrations add <Name> --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext` — never hand-write them. They live in `Ulbra.One.Persistence/Migrations/` and auto-apply at startup (§1).

---

## 6. Pagination

Two list styles, by intent:

| List kind | Examples | Shape | Why |
|---|---|---|---|
| **Browse** (high cardinality) | transactions, vendors, customers, employees | `PaginatedResponse<T>` | unbounded data; must page |
| **Reference** (dropdowns) | companies, units, cost-centers | full `List<TDto>` array | small, fully loaded by the UI selects; paging would just complicate the client |

Browse lists take `[AsParameters] PaginationQueryParameters` (`page` 1-based, `pageSize` clamped to
`[1,100]`, default 20) and call `ToPaginatedAsync` **after** ordering/filtering:

```csharp
var page = await query.OrderBy(v => v.Name).ToPaginatedAsync(pagination, ct);
return TypedResults.Ok(page.Map(VendorResponse.From));   // PaginatedResponse<VendorResponse>
```

`PaginatedResponse<T>(Items, Page, PageSize, Total, TotalPages)` has a `Map` that projects items
while preserving page metadata.

---

## 7. Auth

- **JWT bearer** (`AddJwtAuthentication`) validates issuer/audience/lifetime/signing key with a 1-min
  clock skew. Tokens are minted by `JwtTokenService` with role/name/email claims.
- **Google OAuth** — `POST /api/auth/google` validates a Google ID token, enforces
  `AllowedDomains`, bootstraps admins from `BootstrapAdmins`, upserts the `User`, returns a JWT.
- **`POST /api/auth/dev-token`** — **Development-only** (returns 404 otherwise); mints an admin token
  for local + test use. Both auth endpoints carry `.RequireRateLimiting("auth")`.
- **`UserContext`** (scoped, over `IHttpContextAccessor`) exposes `UserId/UserName/UserEmail/Role`,
  `IsAdmin`, `IsAuthenticated` — read identity from here, not from `HttpContext` directly.
- **Admin gate**: `.RequireAdmin()` (`RequireAdminFilter`) = `.RequireAuthorization()` + a filter
  returning 403 unless the `Admin` role claim is present.

---

## 8. Cross-cutting

- **Rate limiting** — built-in `AddRateLimiter` (`Program.cs`): a named `"auth"` IP-keyed fixed-window
  policy on credential endpoints plus a generous global fallback; 429 returns Problem Details with a
  `Retry-After` header. Configurable under `RateLimiting:*` and **effectively disabled in Development**
  so integration tests / Playwright e2e (which hammer `/auth/dev-token`) never trip 429. It bites in
  Staging/Production.
- **Observability** — `AddAppObservability()`: OpenTelemetry tracing + metrics with ASP.NET Core /
  HttpClient / EF Core / runtime instrumentation. The **OTLP exporter is gated** on
  `OTEL_EXPORTER_OTLP_ENDPOINT` / `Otlp:Endpoint` — absent → no exporter, no collector dependency.
  Structured **JSON console logging** in non-Development; readable console in Development.
- **Typed `HttpClient` lookups** — external integrations are typed clients with base address + timeout:
  ViaCEP (`ICepLookup → ViaCepLookup`) and BrasilAPI CNPJ (`ICnpjLookup → BrasilApiCnpjLookup`),
  registered in `RegistryModule` via `AddHttpClient<TInterface, TImpl>`.
- **Health** — `MapHealthChecks("/health")` with an Npgsql check (excluded from tracing).

---

## 9. Code & naming conventions

- **Code in English, UI strings pt-BR.** Identifiers, OpenAPI `WithTags`, exception/log messages →
  **English**. Only user-facing error-message *content* is localized — and that happens on the
  **frontend**, keyed by the stable error code (§3). Do not hardcode pt-BR in backend code.
- **`TimeProvider`** everywhere, never `DateTime.UtcNow`. `TimeProvider.System` is registered as a
  singleton; inject it (or take it as a handler parameter).
- **Naming glossary** (PT → EN): Company/Unit/CostCenter/Transaction; Vendor/Customer/Employee are
  separate entities. Registry domain entities carry the **`Entity` suffix** (`VendorEntity`,
  `CustomerEntity`, `EmployeeEntity`); response DTOs drop it (`VendorResponse`).
- DTOs are `sealed record` with a static `From(entity)` factory.

---

## 10. Testing & CI

- **xUnit**, two suites:
  - `tests/Ulbra.One.UnitTests` — fast, **no Docker** (entities, validators, `ResultExtensions`,
    `AuditInterceptor`, `DocumentValidator`, `ValidationEndpointFilter`).
  - `tests/Ulbra.One.IntegrationTests` — **Testcontainers Postgres** (`PostgresFixture`) + `ApiFactory`
    (`WebApplicationFactory<Program>` forced to the Development environment). Use
    `ApiFactory.CreateAdminClientAsync()` to get an authenticated client via `/auth/dev-token`.
- `Directory.Build.props` enforces **`net10.0`, nullable enabled, `TreatWarningsAsErrors`** across all
  projects.
- **CI**: `.github/workflows/api-ci.yml` — restore → build (Release) → unit tests → integration tests
  (Testcontainers uses the runner's Docker daemon). Runs on PRs/pushes touching `src/`, `tests/`, the
  props files, or the solution.
- Note: the husky **pre-commit hook only runs frontend** eslint/prettier; backend quality relies on CI.

---

## 11. Anti-patterns (do NOT)

- Add **controllers** — use `IEndpoint`.
- Manual **`.ToTable()` / `.HasColumnName()`** — the snake_case convention resolves naming. (`.SetSchema()` via `e.Metadata.SetSchema("<schema>")` is the only permitted exception — schema only.)
- **Cross-module foreign keys** — reference other modules by loose `Id`; no FK constraints across module schemas.
- **Hand-write migrations** — always `dotnet ef migrations add --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext`.
- **Business logic in endpoints** — keep it in the domain entity/aggregate.
- Return **entities or anonymous objects** from endpoints — map to a response DTO.
- **Hardcode pt-BR** in backend code — English + stable error codes; localization is the frontend's job.
- Use **`DateTime.UtcNow`** — inject `TimeProvider`.
