# Core Module
**Purpose:** Organizational backbone of the ERP — the org chart root. Owns Company (raiz), its Units, and CostCenters shared across modules.
**Schema:** `core`  ·  **UoW key:** "core"
## Boundaries
- Belongs here: `Company`, `Unit`, `CostCenter` (+ `CostCenterUnit` join) aggregates and their CRUD endpoints (`/companies`, `/units`, `/cost-centers`). These are **reference data** — listed as full arrays (dropdowns), not paginated.
- Does NOT belong: financial transactions/payables (Finance), people/parties like vendors & customers (Registry), users & auth (Identity).
- Dependency rule: Domain → (nothing but Common.Domain); Application → Domain + Common.Application (NEVER Infrastructure); Infrastructure → Application + Domain.
## Key files
- `Ulbra.One.Core.Domain/Entities/{Company,Unit,CostCenter,CostCenterUnit}.cs`
- `Ulbra.One.Core.Application/Endpoints/{Companies,Units,CostCenters}/…` (one `IEndpoint` per feature folder)
- `Ulbra.One.Core.Infrastructure/Database/CoreContext.cs` (`Schema = "core"`, `HasDefaultSchema`), `CoreModule.cs` (`AddCoreModule` + UoW key `"core"`)
## Testing
- Unit: `dotnet test tests/Ulbra.One.UnitTests` (entities, validators — no Docker).
- Integration: `dotnet test tests/Ulbra.One.IntegrationTests` (Testcontainers Postgres + `ApiFactory`; use `CreateAdminClientAsync()`).
- After any mapping/entity change: `dotnet ef migrations add <Name> --project src/Modules/Core/Ulbra.One.Core.Infrastructure` (never hand-write); migrations auto-apply at startup. Commit the snapshot.
