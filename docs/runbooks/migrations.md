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

- **snake_case is automatic** (`UseSnakeCaseNamingConvention()`) for **column** names. Configure only
  what the convention can't infer — lengths, `HasPrecision`, indexes, owned types, query filters.
  **No loose `.ToTable()` inside `ConfigureEntity`, no `.HasColumnName()`** — the table name is the
  config's `TableName` property, the column names come from the convention.
- **Schema and table name are declared per entity** by the module's `IEntityTypeConfiguration<T>`,
  which derives from `BaseEntityConfiguration<T>` and overrides `Schema => Schemas.<Module>` **and**
  `TableName` (snake_case, exactly as the table exists in the database) — the base applies both
  (`e.HasAnnotation("Relational:Schema", …)` / `"Relational:TableName"`). There is no global
  `HasDefaultSchema`, and the table name does not emerge from a `DbSet` property name.
- **Commit the snapshot WITH the migration.** `dotnet ef` updates
  `Migrations/AppDbContextModelSnapshot.cs` alongside the new
  `<timestamp>_<Name>.cs` (+ `.Designer.cs`) — all of them go in the same commit. A migration without
  its snapshot update is a bug.
- **One migration per PR.** Don't batch unrelated schema changes; don't edit an already-merged migration —
  add a new one.

## Verify before committing

**Read the generated migration.** If it contains a `RenameTable`, a `RenameIndex` or a constraint
rename you did not intend, the model drifted — a `TableName` that no longer matches the deployed
table, or a naming convention that stopped agreeing with `AppDbContext.NameRewriter`. **Stop and
investigate; do not apply it.** Applied against production those renames are destructive, and the
migration is generated from the model, so it will happily rename a table you never meant to touch.

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
