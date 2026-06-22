# Accounting Module
**Purpose:** Double-entry general ledger (núcleo contábil). Owns the company-scoped chart of accounts (`LedgerAccount`) and the journal (`JournalEntry` + `JournalLine`), plus pure report calculators (trial balance / balancete, DRE, Balanço). Generic/multi-tenant — no business- or ULBRA-specific semantics.
**Schema:** `accounting`  ·  **UoW:** inject `IUnitOfWork` directly (no key — single non-keyed `AppDbContext`-backed UoW)
## Boundaries
- Belongs here: `LedgerAccount` (chart of accounts, per `CompanyId`, synthetic/analytical hierarchy, normal balance); `JournalEntry` (aggregate root, Draft→Posted→Reversed) + `JournalLine`; report calculators (`TrialBalanceCalculator`, `IncomeStatementCalculator`, `BalanceSheetCalculator`).
- Distinct from Finance's `ChartAccount`: that is operational classification for payables; this is the **accounting** chart of accounts for double-entry bookkeeping. They are separate by design.
- Invariants: a posted entry is **immutable** (correct via `Reverse`); Σdebits = Σcredits, ≥2 lines, line amounts > 0; only **active analytical** accounts of the same company receive lines (`LedgerAccount.CanReceiveEntry`). The sequential per-company `Number` is assigned on `Post` — the aggregate accepts the next number as a parameter (the caller computes max+1); kept out of the aggregate for purity.
- Calculators are **pure** (input = `PostedLine` records, no EF/I/O) so they are unit-testable and reusable for consolidated (no company filter) or per-company reports.
- Cross-module refs: `CompanyId`, `CostCenterId`, `UnitId` are loose `Guid` references — no FK constraints to Core. Within the module, `JournalLine → LedgerAccount` is a real FK with `Restrict` (never delete an account with movement).
- Dependency rule: Domain → (nothing but Common.Domain); Application → Domain + Common.Application (NEVER Infrastructure); Infrastructure → Application + Domain.
## Key files
- `Ulbra.One.Accounting.Domain/Entities/{LedgerAccount,JournalEntry,JournalLine}.cs`, `Enums.cs`
- `Ulbra.One.Accounting.Application/Reports/{PostedLine,TrialBalanceCalculator,IncomeStatementCalculator,BalanceSheetCalculator}.cs`
- `Ulbra.One.Accounting.Infrastructure/Database/Configurations/{LedgerAccount,JournalEntry,JournalLine}Configuration.cs` (schema via `Schemas.Accounting`), `AccountingModule.cs`
- Typed ids in `Common.Application/Ids/`: `LedgerAccountId` (`ledgacct_`), `JournalEntryId` (`journal_`), `JournalLineId` (`jline_`). Entities/EF use raw `Guid` (typed ids are the API/DTO wire form, auto-registered for OpenAPI via the source-generated `PrefixedIdRegistry`).
## Testing
- Unit: `dotnet test tests/Ulbra.One.UnitTests` (`Accounting/` — entity invariants + the three calculators incl. the BP `Ativo = Passivo + PL` invariant — no Docker).
- After mapping/entity change: `dotnet ef migrations add <Name> --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext` (never hand-write); commit the snapshot.
- Not yet built (later passes): endpoints, DTOs, BR chart-of-accounts seed, frontend.
