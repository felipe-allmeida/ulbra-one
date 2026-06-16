# Runbook — EF Core migrations

Migrations are **per module**, code-first, auto-applied at startup. **Never hand-write them.**

## Add a migration

`--project` = the module's Infrastructure (where the `DbContext` + `Migrations/` live);
`--startup-project` = the API; `--context` = the module context. Example for Finance:

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/Finance/Ulbra.One.Finance.Infrastructure \
  --startup-project src/API/Ulbra.One.Api \
  --context FinanceContext \
  --output-dir Database/Migrations
```

Swap the project / `--context` per module (`CoreContext`, `IdentityContext`, `RegistryContext`, …).
Name in PascalCase describing the change, e.g. `AddBankAccountsAndPayments`, `PayableApprovalAudit`.

## Rules

- **snake_case is automatic** (`UseSnakeCaseNamingConvention()`). Configure only what the convention
  can't infer — lengths, `HasPrecision`, indexes, owned types, query filters. **No `.ToTable()` /
  `.HasColumnName()`** in `OnModelCreating`.
- Each context has `public const string Schema` + `HasDefaultSchema(Schema)`, so generated migrations
  carry `schema: "finance"` etc.
- **Commit the snapshot WITH the migration.** `dotnet ef` updates
  `Database/Migrations/<Context>ContextModelSnapshot.cs` alongside the new
  `<timestamp>_<Name>.cs` (+ `.Designer.cs`) — all of them go in the same commit. A migration without
  its snapshot update is a bug.
- **One migration per PR.** Don't batch unrelated schema changes; don't edit an already-merged migration —
  add a new one.

## Verify before committing

Confirm the model and the last migration are in sync (no drift):

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/Modules/Finance/Ulbra.One.Finance.Infrastructure \
  --startup-project src/API/Ulbra.One.Api \
  --context FinanceContext
```

Should report **no** pending changes. If it reports pending changes, you edited the model without
generating a migration — add one.

## Applied automatically at startup

`Program.cs` runs each context's `Database.MigrateAsync()` on boot, inside a **Postgres session
advisory lock** (`pg_advisory_lock`) so concurrent instances don't race the schema:

```csharp
// SELECT pg_advisory_lock(@key)  — then, in order:
await sp.GetRequiredService<IdentityContext>().Database.MigrateAsync();
await sp.GetRequiredService<CoreContext>().Database.MigrateAsync();
await sp.GetRequiredService<FinanceContext>().Database.MigrateAsync();
await sp.GetRequiredService<RegistryContext>().Database.MigrateAsync();
// SELECT pg_advisory_unlock(@key)
```

No manual `database update` step in normal dev/deploy — running the API (or `docker compose up`)
applies pending migrations. Integration tests apply them the same way against the Testcontainers DB.
A **new module** must add its `MigrateAsync()` call to this block (see [`new-module.md`](new-module.md)).
