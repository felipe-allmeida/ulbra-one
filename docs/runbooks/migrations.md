# Runbook — EF Core migrations

Migrations are **code-first, auto-applied at startup**. There is a **single `AppDbContext`** in
`Ulbra.One.Persistence` that covers all business modules. **Never hand-write migrations.**

## Add a migration

`--project` = `Ulbra.One.Persistence` (where `AppDbContext` and `Migrations/` live);
`--startup-project` = the API; `--context AppDbContext`. `--output-dir Migrations` keeps them in the
standard location:

```bash
dotnet ef migrations add <Name> \
  --project src/Ulbra.One.Persistence \
  --startup-project src/API/Ulbra.One.Api \
  --context AppDbContext \
  --output-dir Migrations
```

Name in PascalCase describing the change, e.g. `AddBankAccountsAndPayments`, `PayableApprovalAudit`.

> **Audit** keeps its own `AuditContext` (in `Ulbra.One.Audit.Infrastructure`) with its own
> migrations. Use `--context AuditContext` and `--project src/Modules/Audit/Ulbra.One.Audit.Infrastructure`
> only when changing audit-log schema.

## Rules

- **snake_case is automatic** (`UseSnakeCaseNamingConvention()`). Configure only what the convention
  can't infer — lengths, `HasPrecision`, indexes, owned types, query filters. **No `.ToTable()` /
  `.HasColumnName()`** in `OnModelCreating`.
- Schema is set **per entity** inside each module's `IEntityTypeConfiguration<T>` by overriding
  `Schema => Schemas.<Module>` in `BaseEntityConfiguration<T>`. There is no global `HasDefaultSchema`;
  each entity carries its schema through `e.HasAnnotation("Relational:Schema", Schema)`.
- **Commit the snapshot WITH the migration.** `dotnet ef` updates
  `Migrations/AppDbContextModelSnapshot.cs` alongside the new
  `<timestamp>_<Name>.cs` (+ `.Designer.cs`) — all of them go in the same commit. A migration without
  its snapshot update is a bug.
- **One migration per PR.** Don't batch unrelated schema changes; don't edit an already-merged migration —
  add a new one.

## Verify before committing

Confirm the model and the last migration are in sync (no drift):

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/Ulbra.One.Persistence \
  --startup-project src/API/Ulbra.One.Api \
  --context AppDbContext
```

Should report **no** pending changes. If it reports pending changes, you edited the model without
generating a migration — add one.

## Applied automatically at startup

`Program.cs` runs both contexts' `Database.MigrateAsync()` on boot, inside a **Postgres session
advisory lock** (`pg_advisory_lock`) so concurrent instances don't race the schema:

```csharp
// SELECT pg_advisory_lock(@key)  — then, in order:
await sp.GetRequiredService<AppDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<AuditContext>().Database.MigrateAsync();
// SELECT pg_advisory_unlock(@key)
```

No manual `database update` step in normal dev/deploy — running the API (or `docker compose up`)
applies pending migrations. Integration tests apply them the same way against the Testcontainers DB.
