# Runbook — Add a new module (bounded context)

A module = three projects `Domain → Application → Infrastructure`, one DB schema, wired in `Program.cs`.
Model it on **Core** (`CoreContext`) or **Registry** (`RegistryEndpoints.cs`, keyed UoW, validators).
Replace `Billing` / `billing` below with your context name.

## 1. Create the three projects

```
src/Modules/Billing/
  Ulbra.One.Billing.Domain/           # entities, aggregates, value objects — NO infra deps
  Ulbra.One.Billing.Application/       # IEndpoint slices, Contracts (DTOs), Validation, MapBillingEndpoints
  Ulbra.One.Billing.Infrastructure/    # DbContext, Migrations, BillingModule (DI)
```

Add each `.csproj` to the solution (`dotnet sln add …`).

## 2. Layering rule (enforce via ProjectReferences)

- **Domain** → references nothing internal.
- **Application** → references **Domain + `Common.Application` ONLY**. It must **NOT** reference any
  Infrastructure project (this is why `MapBillingEndpoints` lives in Application, not Infrastructure).
- **Infrastructure** → references **Application + Domain + `Common.Infrastructure`**.

So: `Infrastructure → Application → Domain`. Anti-pattern: Application referencing Infrastructure.

## 3. DbContext + schema

In Infrastructure, mirror `CoreContext`:

```csharp
public class BillingContext(DbContextOptions<BillingContext> options) : DbContext(options)
{
    public const string Schema = "billing";
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        // configure lengths/precision/indexes/owned types — NO .ToTable()/.HasColumnName()
        base.OnModelCreating(modelBuilder);
    }
}
```

snake_case naming is handled by the global `UseSnakeCaseNamingConvention()` (set in the module DI).

## 4. Module DI (`BillingModule.cs`)

Copy `FinanceModule.cs`: register the `AuditInterceptor`, the `DbContext` (Npgsql + retry +
`UseSnakeCaseNamingConvention()` + audit interceptor), and the **keyed** unit of work:

```csharp
public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration config)
{
    var cs = config.GetConnectionString("Database") ?? throw new InvalidOperationException("…");
    services.AddScoped<AuditInterceptor>();
    services.AddDbContext<BillingContext>((sp, o) => o.UseNpgsql(cs, n => { n.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null); n.CommandTimeout(30); })
        .UseSnakeCaseNamingConvention().AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
    services.AddKeyedScoped<IUnitOfWork>("billing",
        (sp, _) => new EfUnitOfWork<BillingContext>(sp.GetRequiredService<BillingContext>()));
    return services;
}
```

Handlers then inject `[FromKeyedServices("billing")] IUnitOfWork uow`.

## 5. Endpoints (`MapBillingEndpoints` in Application)

Each operation is a `sealed class : IEndpoint` in its own folder
(`Endpoints/Invoices/CreateInvoice/`). Aggregate them in `BillingEndpoints.cs`:

```csharp
public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder builder)
{
    var invoices = builder.MapGroup("/invoices");
    invoices.MapEndpoint<ListInvoices>();
    invoices.MapEndpoint<CreateInvoice>();
    return builder;
}
```

If the module has validators, also expose `AddBillingValidators()`
(`AddValidatorsFromAssemblyContaining<…>`).

## 6. Wire into `Program.cs`

```csharp
builder.Services.AddBillingModule(builder.Configuration);
builder.Services.AddBillingValidators();          // if any
// …
apiGroup.MapBillingEndpoints();                    // under the /api group
// in the migrate-on-startup block (inside the advisory lock):
await scope.ServiceProvider.GetRequiredService<BillingContext>().Database.MigrateAsync();
```

## 7. Typed IDs

- Add `InvoiceId` (etc.) in `Common.Application/Ids/` (`record struct … : IPrefixedId, IParsable<…>`,
  `Prefix => "invoice_"`, `[JsonConverter(typeof(PrefixedIdJsonConverter<InvoiceId>))]`).
- **Register each in `Program.cs`**: `options.MapType<…InvoiceId>(() => IdSchema("invoice_3fa8…"))`.

## 8. First migration

See [`migrations.md`](migrations.md) — generate the initial migration into
`Billing.Infrastructure/Database/Migrations/`. It auto-applies at startup (step 6).

## 9. Tests

Add unit tests (domain) + integration tests (`ApiFactory`) — see [`testing.md`](testing.md).
