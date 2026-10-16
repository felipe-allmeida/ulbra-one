# Runbook — Add a new module (bounded context)

A module = three projects `Domain → Application → Infrastructure`, one DB schema, wired in `Program.cs`.
Model it on **Core** or **Registry** — they follow the single-context pattern described here.
Replace `Billing` / `billing` below with your context name.

## 1. Create the three projects

```
src/Modules/Billing/
  Ulbra.One.Billing.Domain/           # entities, aggregates, value objects — NO infra deps
  Ulbra.One.Billing.Application/       # IEndpoint slices, Contracts (DTOs), Validation, MapBillingEndpoints
  Ulbra.One.Billing.Infrastructure/    # IEntityTypeConfiguration classes, BillingModule (DI)
```

Add each `.csproj` to the solution (`dotnet sln add …`).

## 2. Layering rule (enforce via ProjectReferences)

- **Domain** → references nothing internal.
- **Application** → references **Domain + `Common.Application` ONLY**. It must **NOT** reference any
  Infrastructure project (this is why `MapBillingEndpoints` lives in Application, not Infrastructure).
- **Infrastructure** → references **Application + Domain + `Common.Infrastructure`**.

So: `Infrastructure → Application → Domain`. Anti-pattern: Application referencing Infrastructure.

## 3. Schema constant + entity configurations

Add the module schema to `Schemas` in `Common.Infrastructure`:

```csharp
// src/Common/Ulbra.One.Common.Infrastructure/Persistence/Schemas.cs
public const string Billing = "billing";
```

In Infrastructure, create an `IEntityTypeConfiguration<T>` for each entity by extending
`BaseEntityConfiguration<T>` (from `Common.Infrastructure`). Override `Schema` and put entity-specific
mapping in `ConfigureEntity`:

```csharp
public sealed class InvoiceConfiguration : BaseEntityConfiguration<Invoice>
{
    protected override string Schema => Schemas.Billing;
    protected override string TableName => "invoices";

    protected override void ConfigureEntity(EntityTypeBuilder<Invoice> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Description).HasMaxLength(300).IsRequired();
        // lengths, precision, indexes, owned types — no .ToTable()/.HasColumnName()
    }
}
```

There is **no per-module `DbContext`**. All entities land in the single `AppDbContext`
(`Ulbra.One.Persistence`). See Core's `CompanyConfiguration` and Registry's `VendorConfiguration`
as living examples.

Every configuration must declare both `Schema` and `TableName` (snake_case, exactly as the table
exists in the database) — the base applies them; the name does not emerge from anywhere else.

## 4. Register the module's model contribution

`Ulbra.One.Persistence` never references a module project — do **not** touch `AppDbContext`. Two
steps instead:

1. Runtime, in `AddBillingModule`:

   ```csharp
   services.AddModelConfiguration(typeof(BillingModule).Assembly);
   ```

2. Design-time, since the EF tooling has no DI container — add the assembly to
   `ModuleModelConfigurations.All` (`src/API/Ulbra.One.Api/Database/`):

   ```csharp
   typeof(BillingModule).Assembly,
   ```

Skipping step 2 makes `ModelRegistrationTests` fail (the two lists are compared), and would
otherwise make the next `migrations add` emit `DROP TABLE` for the module's tables.

There are no `DbSet` properties — handlers reach entities via `IUnitOfWork` / `Set<T>()`.

## 5. Module DI (`BillingModule.cs`)

Register the module's validators and any module-specific services. There is **no per-module
`DbContext`** to register and **no keyed `IUnitOfWork`** — all handlers inject `IUnitOfWork` directly
(single non-keyed registration backed by `AppDbContext`, wired in `AddPersistence`):

```csharp
public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration config)
{
    // register validators, domain services, etc.
    services.AddModelConfiguration(typeof(BillingModule).Assembly);   // step 4
    return services;
}
```

Handlers inject `IUnitOfWork uow` (no `[FromKeyedServices]`).

## 6. Endpoints (`MapBillingEndpoints` in Application)

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

## 7. Wire into `Program.cs`

```csharp
builder.Services.AddBillingModule(builder.Configuration);
builder.Services.AddBillingValidators();          // if any
// …
apiGroup.MapBillingEndpoints();                    // under the /api group
```

No `MigrateAsync` call needed for the new module — `AppDbContext.MigrateAsync()` already covers all
module schemas in the single migration run at startup.

## 8. Typed IDs

Add one file per id in `Common.Application/Ids/` using the `[PrefixedId]` attribute:

```csharp
/// <summary>Strongly-typed, prefixed id for Invoice entities (wire form <c>invoice_{guid:N}</c>).</summary>
[PrefixedId("invoice_")]
public readonly partial record struct InvoiceId(Guid Value);
```

The `PrefixedIdGenerator` (project `Ulbra.One.SourceGenerators`) automatically emits
`IPrefixedId` + `IParsable<T>`, `JsonConverter`, `Prefix`, `New()`, `ToString()`, and
`Parse`/`TryParse`. It also regenerates `PrefixedIdRegistry.g.cs` to include the new id —
`Program.cs` already iterates `PrefixedIdRegistry.All` to register all OpenAPI string schemas, so
**no manual `MapType` call is needed**.

## 9. First migration

See [`migrations.md`](migrations.md) — generate the initial migration against `AppDbContext` in
`Ulbra.One.Persistence/Migrations/`. It auto-applies at startup via the existing `AppDbContext.MigrateAsync()` call.

## 10. Tests

Add unit tests (domain) + integration tests (`ApiFactory`) — see [`testing.md`](testing.md).
