# Finance Module
**Purpose:** Accounts payable + cash/bank lifecycle. Owns the `Payable` aggregate (with installments, payments, attachments), bank accounts, chart of accounts, expense categories, classification, and bank-statement conciliation/aging.
**Schema:** `finance`  ·  **UoW:** inject `IUnitOfWork` directly (no key — single non-keyed `AppDbContext`-backed UoW)
## Boundaries
- Belongs here: `Payable` (aggregate root) + `Installment` + `Payment` + `PayableAttachment`; `BankAccount`, `ChartAccount`, `ExpenseCategory`, `ClassificationPolicy`; conciliation (`BankStatementImport`, `BankTransaction`); aging reports. Endpoints under `/payables`, `/bank-accounts`, `/chart-accounts`, `/expense-categories`, `/classification-policies`, `/conciliation`, `/aging`, `/transactions`.
- **Legacy:** `Transaction` is the old generic finance entity, superseded by the `Payable` aggregate (see `TODO(AEL-579)` in `Transaction.cs`). Do not extend it; new payable work goes through `Payable`.
- Recent invariants (AEL-579): **optimistic concurrency via Postgres `xmin`** on `Payable` + `Installment` (→ 409 on conflict); **payments are append-only** — a payable with payments cannot be edited, payment FK is `Restrict`; payment components non-negative, no overpayment beyond balance. Preserve these.
- Does NOT belong: Company/Unit/CostCenter (Core), vendors/customers (Registry), users/auth (Identity).
- Cross-module refs: `VendorId`, `CompanyId`, `CostCenterId` on `Payable` are loose `Guid` references — no FK constraints to Core/Registry schemas.
- Dependency rule: Domain → (nothing but Common.Domain); Application → Domain + Common.Application (NEVER Infrastructure); Infrastructure → Application + Domain.
## Key files
- `Ulbra.One.Finance.Domain/Entities/{Payable,Installment,Payment,PayableAttachment,BankAccount,ChartAccount,ExpenseCategory,ClassificationPolicy,BankStatementImport,BankTransaction,Transaction}.cs`, `Enums.cs`
- `Ulbra.One.Finance.Application/Endpoints/Payables/…` (CreatePayable, RegisterPayment, ReversePayment, Approve/Reject/Cancel, Classify, Upload/DownloadAttachment, …)
- `Ulbra.One.Finance.Infrastructure/Database/{PayableConfiguration,InstallmentConfiguration,…}.cs` (schema set via `e.Metadata.SetSchema("finance")`; `xmin` rowversion config), `FinanceModule.cs`
## Testing
- Unit: `dotnet test tests/Ulbra.One.UnitTests` (Payable/Payment invariants, validators — no Docker).
- Integration: `dotnet test tests/Ulbra.One.IntegrationTests` (Testcontainers Postgres; covers 409 concurrency + append-only payment paths). Use `ApiFactory.CreateAdminClientAsync()`.
- After mapping/entity change: `dotnet ef migrations add <Name> --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext` (never hand-write); commit the snapshot. Note `xmin` is Postgres' hidden column — no DDL is generated for it.
