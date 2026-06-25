# Finance Stage-Summary + Audit-Trail Endpoints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the four backend endpoints the payables/receivables UX overhaul needs — `stage-summary` (count + total per stage) and `audit-trail` (typed chronological events) for both payables and receivables — plus a reusable `IUserDirectory` for resolving actor names.

**Architecture:** Pure additive work in the .NET Finance module, following the existing `IEndpoint` minimal-API pattern. Stage-summary endpoints aggregate in the database via `GROUP BY` (modeled on the existing aging endpoint). Audit-trail endpoints synthesize typed events from each aggregate's explicit audit fields (hybrid source "C"): entity fields are the primary source; the two actions that lack an entity timestamp (`Payable.Cancel`, `Payment.Reverse`) get their precise time from the `AuditLog` table; actor display names resolve through a new `IUserDirectory` abstraction (interface in `Common.Application`, implementation in `Identity.Infrastructure`) so Finance never references the Identity domain. The trail-building logic is a pure function unit-tested in isolation; endpoints get integration tests against Postgres.

**Tech Stack:** C# / .NET 10, EF Core (single `AppDbContext`, `IUnitOfWork.ReadSet<T>()`), minimal APIs (`IEndpoint`), xUnit + `WebApplicationFactory` (`ApiFactory`) integration tests + Testcontainers Postgres, `openapi-typescript` for the FE client.

## Global Constraints

- Endpoints use the `IEndpoint` minimal-API pattern (no controllers), return `TypedResults.*`, grouped with `.WithTags("Finance")` and `.RequireAuthorization()`. (docs/backend-conventions.md)
- Reads use `IUnitOfWork.ReadSet<T>()` (no-tracking). Audit log reads use the keyed UoW `[FromKeyedServices("audit")] IUnitOfWork`. (CLAUDE.md)
- No `DateTime.UtcNow` — inject `TimeProvider` and call `clock.GetUtcNow()`. (memory: feedback_dotnet-conventions)
- Cross-module references by `Id` only — Finance must NOT reference the Identity domain. Name resolution goes through `IUserDirectory` in `Common.Application`. (CLAUDE.md)
- Money is `decimal`. Typed IDs (`PayableId`, `CompanyId`, …) wrap `Guid Value`; minimal API binds them from route/query via the generated `IParsable`. (docs/api-design.md)
- All identifiers/comments in English. Tests for everything — no exceptions. (memory: feedback_always-tests)
- Response DTOs are `sealed record`s in `…/Finance.Application/Contracts/`; enums serialize as strings (global `JsonStringEnumConverter`).
- After the endpoints land, regenerate `web/openapi.json` from the running API and `web/src/api/schema.d.ts` from it. (docs/frontend-conventions.md)

**Verified reference signatures (from the existing codebase — use verbatim):**
- `PayableStatus { Received, PendingClassification, PendingApproval, Scheduled, Paid, Rejected, Cancelled }`
- `InstallmentStatus { Open, PartiallyPaid, Paid, InRemittance }`
- `ReceivableStatus { AwaitingReceipt, Received, Cancelled, WrittenOff }`
- `ReceivableInstallmentStatus { Open, PartiallyReceived, Received }`
- `Payable`: `decimal TotalAmount`, `PayableStatus Status`, `IReadOnlyList<Installment> Installments`, `IReadOnlyList<PayableAttachment> Attachments`, `string CreatedBy`, `DateTimeOffset CreatedAt`, `string? ApprovedBy`, `DateTimeOffset? ApprovedAt`, `string? RejectedBy`, `DateTimeOffset? RejectedAt`, `string? RejectionReason`, `string? CancelReason`, `Guid? AccountingAccountId`, `Guid? FinancialAccountId`, `string UpdatedBy`, `DateTimeOffset UpdatedAt`. Installments + their Payments are `AutoInclude()`d; `Attachments` are not.
- `Installment`: `int Sequence`, `DateOnly DueDate`, `decimal Amount`, `decimal PaidAmount`, `InstallmentStatus Status`, `IReadOnlyList<Payment> Payments`.
- `Payment`: `Guid Id`, `Guid InstallmentId`, `DateOnly PaymentDate`, `decimal Amount`, `bool IsReversed`, `string? ReversalReason`, `DateTimeOffset CreatedAt`, `Guid CreatedById`. (No `ReversedAt`.)
- `PayableAttachment`: `Guid Id`, `string FileName`, `DateTimeOffset CreatedAt`, `Guid UploadedById` (verify exact name in Task 4; the response contract `PayableAttachmentResponse` already exposes it).
- `Receivable`: `decimal TotalAmount`, `ReceivableStatus Status`, `IReadOnlyList<ReceivableInstallment> Installments` (NOT auto-included — must `.Include`), `string CreatedBy`, `DateTimeOffset CreatedAt`, `string? CancelReason`, `Guid? CancelledById`, `DateTimeOffset? CancelledAt`, `string? WriteOffReason`.
- `ReceivableInstallment`: `int Sequence`, `DateOnly DueDate`, `decimal Amount`, `decimal ReceivedAmount`, `ReceivableInstallmentStatus Status`, `IReadOnlyList<Receipt> Receipts` (auto-included).
- `Receipt`: `Guid Id`, `Guid InstallmentId`, `DateOnly ReceiptDate`, `decimal Amount`, `bool IsReversed`, `string? ReversalReason`, `DateTimeOffset CreatedAt`, `Guid CreatedById`, `DateTimeOffset? ReversedAt`, `Guid? ReversedById`.
- `AuditLog` (in `audit` schema, read via keyed UoW): `DateTimeOffset OccurredAt`, `string ActorUserId`, `AuditAction Action {Create,Update,Delete}`, `string EntityType`, `string TableName`, `string PrimaryKey`, `string? OldValues`, `string? NewValues`, `string? AffectedColumns`.
- `User` (Identity.Domain): `Guid Id`, `string Name`, `string Email`.
- `IEndpoint { void Map(IEndpointRouteBuilder builder); }`; registered in `FinanceEndpoints.cs` via `group.MapEndpoint<T>()`.
- `IUnitOfWork.ReadSet<T>()` / `.Set<T>()`.
- Integration tests: `[Collection("Postgres")]`, ctor takes `PostgresFixture fixture`, `await using var factory = new ApiFactory(fixture.ConnectionString); var client = await factory.CreateAdminClientAsync();`. Each test file seeds via the public HTTP API.

---

### Task 1: `IUserDirectory` abstraction + Identity implementation

A reusable service that maps a set of user GUIDs to display names, so audit-trail endpoints can show actor names without Finance referencing the Identity domain.

**Files:**
- Create: `src/Common/Ulbra.One.Common.Application/Authentication/IUserDirectory.cs`
- Create: `src/Modules/Identity/Ulbra.One.Identity.Infrastructure/UserDirectory.cs`
- Modify: `src/Modules/Identity/Ulbra.One.Identity.Infrastructure/IdentityModule.cs` (register the service)
- Test: `tests/Ulbra.One.IntegrationTests/Identity/UserDirectoryTests.cs`

**Interfaces:**
- Produces:
  - `namespace Ulbra.One.Common.Application.Authentication; public interface IUserDirectory { Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct); }`
  - `UserDirectory : IUserDirectory` (injects `IUnitOfWork`), registered as scoped.

- [ ] **Step 1: Write the failing integration test**

Create `tests/Ulbra.One.IntegrationTests/Identity/UserDirectoryTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Identity;

[Collection("Postgres")]
public class UserDirectoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GetNamesAsync_resolves_known_ids_and_omits_unknown()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        // CreateAdminClientAsync provisions the admin user row (admin@ulbra.br / "Admin Teste").
        await factory.CreateAdminClientAsync();

        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var admin = await uow.ReadSet<User>().FirstAsync(u => u.Email == "admin@ulbra.br");
        var unknown = Guid.NewGuid();

        var names = await directory.GetNamesAsync(new[] { admin.Id, unknown }, CancellationToken.None);

        Assert.Equal("Admin Teste", names[admin.Id]);
        Assert.False(names.ContainsKey(unknown));
    }

    [Fact]
    public async Task GetNamesAsync_empty_input_returns_empty()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        await factory.CreateAdminClientAsync();
        using var scope = factory.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var names = await directory.GetNamesAsync(Array.Empty<Guid>(), CancellationToken.None);

        Assert.Empty(names);
    }
}
```

(Note: `using Microsoft.EntityFrameworkCore;` is also required for `FirstAsync` — add it if the file doesn't compile.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~UserDirectoryTests`
Expected: FAIL to compile — `IUserDirectory` does not exist yet.

- [ ] **Step 3: Create the interface**

Create `src/Common/Ulbra.One.Common.Application/Authentication/IUserDirectory.cs`:

```csharp
namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Resolves user display names by id without forcing callers to reference the Identity module's
/// domain. Implemented in Identity.Infrastructure; consumed across modules (audit trails, activity feeds).
/// </summary>
public interface IUserDirectory
{
    /// <summary>Maps each known user id to its display name. Unknown ids are omitted from the result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
```

- [ ] **Step 4: Create the implementation**

Create `src/Modules/Identity/Ulbra.One.Identity.Infrastructure/UserDirectory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure;

/// <summary>Reads user display names from the single AppDbContext via the shared unit of work.</summary>
public sealed class UserDirectory(IUnitOfWork uow) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, string>();
        var distinct = userIds.Distinct().ToList();
        return await uow.ReadSet<User>()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }
}
```

- [ ] **Step 5: Register the service**

In `src/Modules/Identity/Ulbra.One.Identity.Infrastructure/IdentityModule.cs`, next to the existing registrations (`services.AddScoped<UserContext>();` around line 19), add:

```csharp
        services.AddScoped<IUserDirectory, UserDirectory>();
```

Add `using Ulbra.One.Common.Application.Authentication;` to the file's usings if not already present.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~UserDirectoryTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Common/Ulbra.One.Common.Application/Authentication/IUserDirectory.cs \
        src/Modules/Identity/Ulbra.One.Identity.Infrastructure/UserDirectory.cs \
        src/Modules/Identity/Ulbra.One.Identity.Infrastructure/IdentityModule.cs \
        tests/Ulbra.One.IntegrationTests/Identity/UserDirectoryTests.cs
git commit -m "feat(AEL-621): IUserDirectory — cross-module user name resolution"
```

---

### Task 2: Payable stage-summary endpoint

`GET /api/payables/stage-summary?companyId=…` returning `{ entry, classification, approval, payment, reconciled }`, each `{ count, totalAmount }`. Counts/totals are over **payables** (not installments): each payable contributes 1 to the count of its stage and its `TotalAmount` to that stage's total.

**Files:**
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/PayableStageSummaryResponse.cs`
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Payables/StageSummary/GetPayableStageSummary.cs`
- Modify: `src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs` (register endpoint)
- Test: `tests/Ulbra.One.IntegrationTests/Finance/PayableStageSummaryTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `public sealed record StageBucket(int Count, decimal TotalAmount);`
  - `public sealed record PayableStageSummaryResponse(StageBucket Entry, StageBucket Classification, StageBucket Approval, StageBucket Payment, StageBucket Reconciled);`
  - Endpoint `GetPayableStageSummary : IEndpoint`, route `GET stage-summary` under the `/payables` group, returns `PayableStageSummaryResponse`.
- Stage→status mapping: Entry=`Received`, Classification=`PendingClassification`, Approval=`PendingApproval`, Payment=`Scheduled`, Reconciled=`Paid`. (Rejected/Cancelled are excluded from all buckets.)

- [ ] **Step 1: Write the failing integration test**

Create `tests/Ulbra.One.IntegrationTests/Finance/PayableStageSummaryTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableStageSummaryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StageSummary_groups_count_and_total_by_stage()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor A");

        // Two payables left in the default 'Received' stage (Entry); one approved -> Scheduled (Payment).
        await FinanceSeed.CreatePayableAsync(client, companyId, vendor, cc, today.AddDays(10), 100m, approve: false);
        await FinanceSeed.CreatePayableAsync(client, companyId, vendor, cc, today.AddDays(10), 200m, approve: false);
        await FinanceSeed.CreatePayableAsync(client, companyId, vendor, cc, today.AddDays(10), 50m, approve: true);

        var res = await client.GetAsync($"/api/payables/stage-summary?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var entry = body.GetProperty("entry");
        Assert.Equal(2, entry.GetProperty("count").GetInt32());
        Assert.Equal(300m, entry.GetProperty("totalAmount").GetDecimal());

        var payment = body.GetProperty("payment");
        Assert.Equal(1, payment.GetProperty("count").GetInt32());
        Assert.Equal(50m, payment.GetProperty("totalAmount").GetDecimal());

        // Stages with no payables report zero, not null.
        Assert.Equal(0, body.GetProperty("classification").GetProperty("count").GetInt32());
        Assert.Equal(0m, body.GetProperty("reconciled").GetProperty("totalAmount").GetDecimal());
    }
}
```

This test depends on a shared `FinanceSeed` helper (HTTP seeding via the public API). If `tests/Ulbra.One.IntegrationTests/Finance/FinanceSeed.cs` does not already exist, create it in this step by extracting the seeding helpers used by `AgingEndpointsTests.cs` (`CreateCompanyAsync`, `CreateCostCenterAsync`, `CreateVendorAsync`, `CreatePayableAsync(..., bool approve)`, `CreateBankAccountAsync`, `RegisterPaymentAsync`, `RandomCnpj`) into a `public static class FinanceSeed` in the `Ulbra.One.IntegrationTests.Finance` namespace, and update `AgingEndpointsTests.cs` to call `FinanceSeed.*`. Copy the method bodies verbatim from `AgingEndpointsTests.cs` — do not paraphrase the HTTP payloads. `CreatePayableAsync` must return the created payable id (string) and, when `approve: true`, classify + approve it so the payable reaches `Scheduled`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~PayableStageSummaryTests`
Expected: FAIL — route `stage-summary` returns 404 (endpoint not mapped).

- [ ] **Step 3: Create the response contract**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/PayableStageSummaryResponse.cs`:

```csharp
namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Count of titles and the sum of their total amounts for one workflow stage.</summary>
public sealed record StageBucket(int Count, decimal TotalAmount);

/// <summary>Pending-work summary per payable workflow stage, keyed by the UX stage names.</summary>
public sealed record PayableStageSummaryResponse(
    StageBucket Entry,
    StageBucket Classification,
    StageBucket Approval,
    StageBucket Payment,
    StageBucket Reconciled);
```

- [ ] **Step 4: Create the endpoint**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Payables/StageSummary/GetPayableStageSummary.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.StageSummary;

public sealed class GetPayableStageSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-summary", Handle).WithName("GetPayableStageSummary").WithTags("Finance")
            .RequireAuthorization().Produces<PayableStageSummaryResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(CompanyId companyId, IUnitOfWork uow, CancellationToken ct)
    {
        // One row per non-terminal stage: COUNT of payables and SUM of their TotalAmount, grouped by Status.
        var rows = await uow.ReadSet<Payable>()
            .Where(p => p.CompanyId == companyId.Value)
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(p => p.TotalAmount) })
            .ToListAsync(ct);

        StageBucket Bucket(PayableStatus s)
        {
            var r = rows.FirstOrDefault(x => x.Status == s);
            return r is null ? new StageBucket(0, 0m) : new StageBucket(r.Count, r.Total);
        }

        return TypedResults.Ok(new PayableStageSummaryResponse(
            Entry: Bucket(PayableStatus.Received),
            Classification: Bucket(PayableStatus.PendingClassification),
            Approval: Bucket(PayableStatus.PendingApproval),
            Payment: Bucket(PayableStatus.Scheduled),
            Reconciled: Bucket(PayableStatus.Paid)));
    }
}
```

(If the `using Ulbra.One.Common.Application;` namespace is wrong for `IEndpoint`, match the exact using lines from `Endpoints/Aging/GetAgingReport/GetAgingReport.cs` — that file is the canonical reference.)

- [ ] **Step 5: Register the endpoint**

In `src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs`, find the payables route group (the `MapGroup("/payables")` used by `ListPayables`/`GetPayable`; the aging group is `MapGroup("/payables/aging")`). Register the new endpoint on the **payables** group so the route is `/payables/stage-summary`:

```csharp
        payables.MapEndpoint<GetPayableStageSummary>();
```

Add `using Ulbra.One.Finance.Application.Endpoints.Payables.StageSummary;` to the file. Use the exact local variable name of the payables group as it appears in the file (it may be `payables`, `p`, or inline `builder.MapGroup("/payables")`). If endpoints are registered directly on an inline group, add the line consistently with the neighbors.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~PayableStageSummaryTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/PayableStageSummaryResponse.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Payables/StageSummary/GetPayableStageSummary.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs \
        tests/Ulbra.One.IntegrationTests/Finance/PayableStageSummaryTests.cs \
        tests/Ulbra.One.IntegrationTests/Finance/FinanceSeed.cs \
        tests/Ulbra.One.IntegrationTests/Finance/AgingEndpointsTests.cs
git commit -m "feat(AEL-621): payable stage-summary endpoint"
```

---

### Task 3: Receivable stage-summary endpoint

`GET /api/receivables/stage-summary?companyId=…` returning `{ entry, receipt, reconciled }`. Unlike payables, Entry vs Receipt is a split of the same backend status `AwaitingReceipt`: **Entry** = receivables where every installment has `ReceivedAmount == 0` (nothing received yet — still triage); **Receipt** = `AwaitingReceipt` receivables where at least one installment has `ReceivedAmount > 0` (partial receipt in progress). **Reconciled** = `Received`. (Cancelled/WrittenOff excluded.)

**Files:**
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/ReceivableStageSummaryResponse.cs`
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Receivables/StageSummary/GetReceivableStageSummary.cs`
- Modify: `src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs`
- Test: `tests/Ulbra.One.IntegrationTests/Finance/ReceivableStageSummaryTests.cs`

**Interfaces:**
- Consumes: `StageBucket` from Task 2 (`Ulbra.One.Finance.Application.Contracts.StageBucket`).
- Produces:
  - `public sealed record ReceivableStageSummaryResponse(StageBucket Entry, StageBucket Receipt, StageBucket Reconciled);`
  - Endpoint `GetReceivableStageSummary : IEndpoint`, route `GET stage-summary` under `/receivables`.

- [ ] **Step 1: Write the failing integration test**

Create `tests/Ulbra.One.IntegrationTests/Finance/ReceivableStageSummaryTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ReceivableStageSummaryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StageSummary_splits_entry_and_receipt_by_received_amount()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var customer = await FinanceSeed.CreateCustomerAsync(client, companyId, "Cliente A");
        var bank = await FinanceSeed.CreateBankAccountAsync(client, companyId, "00012345");

        // Two untouched (Entry); one with a partial receipt (Receipt).
        await FinanceSeed.CreateReceivableAsync(client, companyId, customer, cc, today.AddDays(10), 100m);
        await FinanceSeed.CreateReceivableAsync(client, companyId, customer, cc, today.AddDays(10), 200m);
        var (rcv, inst) = await FinanceSeed.CreateReceivableAsync(client, companyId, customer, cc, today.AddDays(10), 300m);
        await FinanceSeed.RegisterReceiptAsync(client, rcv, inst, bank, 120m, today);

        var res = await client.GetAsync($"/api/receivables/stage-summary?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, body.GetProperty("entry").GetProperty("count").GetInt32());
        Assert.Equal(300m, body.GetProperty("entry").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1, body.GetProperty("receipt").GetProperty("count").GetInt32());
        Assert.Equal(300m, body.GetProperty("receipt").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0, body.GetProperty("reconciled").GetProperty("count").GetInt32());
    }
}
```

If `FinanceSeed` lacks `CreateCustomerAsync`, `CreateReceivableAsync` (returns `(receivableId, firstInstallmentId)`), or `RegisterReceiptAsync`, add them in this step by copying the corresponding HTTP payloads from `ReceivableEndpointsTests.cs` verbatim into `FinanceSeed`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~ReceivableStageSummaryTests`
Expected: FAIL — 404 (endpoint not mapped).

- [ ] **Step 3: Create the response contract**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/ReceivableStageSummaryResponse.cs`:

```csharp
namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Pending-work summary per receivable workflow stage. Entry/Receipt split the AwaitingReceipt status.</summary>
public sealed record ReceivableStageSummaryResponse(
    StageBucket Entry,
    StageBucket Receipt,
    StageBucket Reconciled);
```

- [ ] **Step 4: Create the endpoint**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Receivables/StageSummary/GetReceivableStageSummary.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.StageSummary;

public sealed class GetReceivableStageSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-summary", Handle).WithName("GetReceivableStageSummary").WithTags("Finance")
            .RequireAuthorization().Produces<ReceivableStageSummaryResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(CompanyId companyId, IUnitOfWork uow, CancellationToken ct)
    {
        // Reconciled: simple count/sum over Received.
        var reconciled = await uow.ReadSet<Receivable>()
            .Where(r => r.CompanyId == companyId.Value && r.Status == ReceivableStatus.Received)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(r => r.TotalAmount) })
            .FirstOrDefaultAsync(ct);

        // AwaitingReceipt split by whether ANY installment already has a received amount.
        var awaiting = await uow.ReadSet<Receivable>()
            .Where(r => r.CompanyId == companyId.Value && r.Status == ReceivableStatus.AwaitingReceipt)
            .Select(r => new { r.TotalAmount, HasReceipt = r.Installments.Any(i => i.ReceivedAmount > 0m) })
            .ToListAsync(ct);

        var entry = awaiting.Where(x => !x.HasReceipt).ToList();
        var receipt = awaiting.Where(x => x.HasReceipt).ToList();

        return TypedResults.Ok(new ReceivableStageSummaryResponse(
            Entry: new StageBucket(entry.Count, entry.Sum(x => x.TotalAmount)),
            Receipt: new StageBucket(receipt.Count, receipt.Sum(x => x.TotalAmount)),
            Reconciled: reconciled is null ? new StageBucket(0, 0m) : new StageBucket(reconciled.Count, reconciled.Total)));
    }
}
```

- [ ] **Step 5: Register the endpoint**

In `FinanceEndpoints.cs`, on the `/receivables` route group, add:

```csharp
        receivables.MapEndpoint<GetReceivableStageSummary>();
```

Add `using Ulbra.One.Finance.Application.Endpoints.Receivables.StageSummary;`. Match the exact receivables-group variable name used in the file.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~ReceivableStageSummaryTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/ReceivableStageSummaryResponse.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Receivables/StageSummary/GetReceivableStageSummary.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs \
        tests/Ulbra.One.IntegrationTests/Finance/ReceivableStageSummaryTests.cs \
        tests/Ulbra.One.IntegrationTests/Finance/FinanceSeed.cs
git commit -m "feat(AEL-621): receivable stage-summary endpoint"
```

---

### Task 4: Payable audit-trail — event contract + pure builder

The typed-event model and a **pure** builder that synthesizes events from a loaded `Payable` plus two injected timestamp lookups (so it unit-tests with no database). The endpoint wiring is Task 5.

**Files:**
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/AuditTrailEventResponse.cs`
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/PayableAuditTrailBuilder.cs`
- Test: `tests/Ulbra.One.UnitTests/Finance/PayableAuditTrailBuilderTests.cs`

**Interfaces:**
- Consumes: `Payable` aggregate (Task constraints list its fields).
- Produces:
  - `public sealed record AuditTrailEventResponse(string Type, string? ActorId, string ActorName, DateTimeOffset Timestamp, IReadOnlyDictionary<string, string> Metadata);`
  - `public static class PayableAuditTrailBuilder` with:
    `public static IReadOnlyList<AuditTrailEventResponse> Build(Payable payable, IReadOnlyDictionary<Guid, string> userNames, DateTimeOffset? cancelledAt, IReadOnlyDictionary<Guid, DateTimeOffset> paymentReversalTimes)`
  - Event `Type` values (strings, matching the FE `workflow.timeline.*` keys): `Created`, `Classified`, `Approved`, `Rejected`, `Cancelled`, `PaymentRegistered`, `PaymentReversed`. Events are returned sorted ascending by `Timestamp`.
  - Actor handling: payable lifecycle actors (`CreatedBy`, `ApprovedBy`, `RejectedBy`) are stored as **strings** (user-id strings); payment actors (`CreatedById`) are **Guids**. `ActorName` = the resolved name when the actor parses as a known Guid, else the raw actor string, else `"Sistema"` when null/empty. `ActorId` = the raw actor string (or the Guid's string form) or null.

- [ ] **Step 1: Write the failing unit test**

Create `tests/Ulbra.One.UnitTests/Finance/PayableAuditTrailBuilderTests.cs`:

```csharp
using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class PayableAuditTrailBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_emits_created_then_classified_then_approved_in_chronological_order()
    {
        var actor = Guid.NewGuid();
        var payable = PayableTestFactory.Scheduled(actorId: actor.ToString(), createdAt: T0,
            approvedAt: T0.AddDays(2), accountingAccountId: Guid.NewGuid(), financialAccountId: Guid.NewGuid());
        var names = new Dictionary<Guid, string> { [actor] = "Maria Santos" };

        var events = PayableAuditTrailBuilder.Build(payable, names, cancelledAt: null,
            paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>());

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e => Assert.Equal("Classified", e.Type),
            e => Assert.Equal("Approved", e.Type));
        Assert.True(events[0].Timestamp <= events[1].Timestamp && events[1].Timestamp <= events[2].Timestamp);
        Assert.Equal("Maria Santos", events[0].ActorName); // resolved from the Guid actor id
    }

    [Fact]
    public void Build_uses_injected_cancelledAt_when_payable_was_cancelled()
    {
        var payable = PayableTestFactory.Cancelled(createdAt: T0, reason: "duplicado");
        var cancelledAt = T0.AddDays(1);

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, string>(),
            cancelledAt: cancelledAt, paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>());

        var cancel = Assert.Single(events, e => e.Type == "Cancelled");
        Assert.Equal(cancelledAt, cancel.Timestamp);
        Assert.Equal("duplicado", cancel.Metadata["reason"]);
    }

    [Fact]
    public void Build_falls_back_to_System_actor_name_when_actor_is_empty()
    {
        var payable = PayableTestFactory.Received(actorId: "", createdAt: T0);

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, string>(),
            cancelledAt: null, paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>());

        Assert.Equal("Sistema", events[0].ActorName);
    }
}
```

This needs a `PayableTestFactory` that builds `Payable` aggregates in specific states for unit testing. If one does not already exist under `tests/Ulbra.One.UnitTests/Finance/`, create `tests/Ulbra.One.UnitTests/Finance/PayableTestFactory.cs` in this step. Inspect the existing `tests/Ulbra.One.UnitTests/Finance/PayableTests.cs` to learn how a `Payable` is constructed in unit tests (constructor signature + `MarkCreated`/`Classify`/`Approve`/`Cancel` calls), and expose static factory methods:
- `Received(string actorId, DateTimeOffset createdAt)` → `new Payable(...)` then `MarkCreated(actorId, createdAt)`.
- `Scheduled(string actorId, DateTimeOffset createdAt, DateTimeOffset approvedAt, Guid accountingAccountId, Guid financialAccountId)` → created, `Classify(accountingAccountId, financialAccountId)`, `Approve(actorId, approvedAt)`.
- `Cancelled(DateTimeOffset createdAt, string reason)` → created then `Cancel(reason)`.
Use the real constructor and method signatures from `Payable.cs` — do not invent parameters.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Ulbra.One.UnitTests --filter FullyQualifiedName~PayableAuditTrailBuilderTests`
Expected: FAIL to compile — `AuditTrailEventResponse` / `PayableAuditTrailBuilder` do not exist.

- [ ] **Step 3: Create the event contract**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/AuditTrailEventResponse.cs`:

```csharp
namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One typed event on a title's audit timeline. <c>Type</c> matches the FE i18n keys under workflow.timeline.*.
/// <c>Metadata</c> carries small string extras (reason, amount, installment sequence) for display.
/// </summary>
public sealed record AuditTrailEventResponse(
    string Type,
    string? ActorId,
    string ActorName,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Metadata);
```

- [ ] **Step 4: Create the builder**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/PayableAuditTrailBuilder.cs`:

```csharp
using System.Globalization;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.AuditTrail;

/// <summary>
/// Synthesizes a payable's typed audit timeline from its explicit audit fields (hybrid source "C").
/// Two actions lack an entity timestamp — Payable.Cancel and Payment.Reverse — so the caller supplies
/// those times from the AuditLog (falling back to a sensible default when absent).
/// </summary>
public static class PayableAuditTrailBuilder
{
    private const string SystemActorName = "Sistema";

    public static IReadOnlyList<AuditTrailEventResponse> Build(
        Payable payable,
        IReadOnlyDictionary<Guid, string> userNames,
        DateTimeOffset? cancelledAt,
        IReadOnlyDictionary<Guid, DateTimeOffset> paymentReversalTimes)
    {
        var events = new List<AuditTrailEventResponse>();

        // Created — actor stored as a user-id string.
        events.Add(Event("Created", payable.CreatedBy, payable.CreatedAt, userNames, new Dictionary<string, string>()));

        // Classified — no dedicated timestamp; the classification settled by the time the title left
        // the classification stage. Use UpdatedAt as the best available marker when the title is classified.
        if (payable.IsClassified)
        {
            events.Add(Event("Classified", payable.UpdatedBy, payable.UpdatedAt, userNames,
                new Dictionary<string, string>()));
        }

        if (payable.ApprovedAt is { } approvedAt)
        {
            events.Add(Event("Approved", payable.ApprovedBy, approvedAt, userNames, new Dictionary<string, string>()));
        }

        if (payable.RejectedAt is { } rejectedAt)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(payable.RejectionReason)) meta["reason"] = payable.RejectionReason;
            events.Add(Event("Rejected", payable.RejectedBy, rejectedAt, userNames, meta));
        }

        if (payable.Status == PayableStatus.Cancelled)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(payable.CancelReason)) meta["reason"] = payable.CancelReason;
            // Cancel has no entity timestamp; use the AuditLog-sourced time, else fall back to UpdatedAt.
            events.Add(Event("Cancelled", payable.UpdatedBy, cancelledAt ?? payable.UpdatedAt, userNames, meta));
        }

        foreach (var installment in payable.Installments)
        {
            foreach (var payment in installment.Payments)
            {
                var regMeta = new Dictionary<string, string>
                {
                    ["installment"] = installment.Sequence.ToString(CultureInfo.InvariantCulture),
                    ["amount"] = payment.Amount.ToString(CultureInfo.InvariantCulture),
                };
                events.Add(EventByGuid("PaymentRegistered", payment.CreatedById, payment.CreatedAt, userNames, regMeta));

                if (payment.IsReversed)
                {
                    var revMeta = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(payment.ReversalReason)) revMeta["reason"] = payment.ReversalReason;
                    // Payment.Reverse has no timestamp; use the AuditLog-sourced time, else fall back to the payment's CreatedAt.
                    var when = paymentReversalTimes.TryGetValue(payment.Id, out var t) ? t : payment.CreatedAt;
                    events.Add(EventByGuid("PaymentReversed", payment.CreatedById, when, userNames, revMeta));
                }
            }
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    // Actor stored as a free string user-id (Payable lifecycle fields).
    private static AuditTrailEventResponse Event(
        string type, string? actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> meta)
    {
        string actorName = SystemActorName;
        if (!string.IsNullOrEmpty(actorId))
        {
            actorName = Guid.TryParse(actorId, out var g) && names.TryGetValue(g, out var n) ? n : actorId;
        }
        return new AuditTrailEventResponse(type, string.IsNullOrEmpty(actorId) ? null : actorId, actorName, at, meta);
    }

    // Actor stored as a Guid (Payment).
    private static AuditTrailEventResponse EventByGuid(
        string type, Guid actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> meta)
    {
        var actorName = names.TryGetValue(actorId, out var n) ? n : SystemActorName;
        return new AuditTrailEventResponse(type, actorId.ToString(), actorName, at, meta);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Ulbra.One.UnitTests --filter FullyQualifiedName~PayableAuditTrailBuilderTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Finance/Ulbra.One.Finance.Application/Contracts/AuditTrailEventResponse.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/PayableAuditTrailBuilder.cs \
        tests/Ulbra.One.UnitTests/Finance/PayableAuditTrailBuilderTests.cs \
        tests/Ulbra.One.UnitTests/Finance/PayableTestFactory.cs
git commit -m "feat(AEL-621): payable audit-trail event contract + pure builder"
```

---

### Task 5: Payable audit-trail endpoint

`GET /api/payables/{id}/audit-trail` — loads the payable (with installments/payments + attachments), gathers the two AuditLog-sourced timestamps, resolves actor names via `IUserDirectory`, runs the Task 4 builder, and adds attachment events.

**Files:**
- Modify: `src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/PayableAuditTrailBuilder.cs` (add attachment events — see Step 3)
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Payables/AuditTrail/GetPayableAuditTrail.cs`
- Modify: `src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs`
- Test: `tests/Ulbra.One.IntegrationTests/Finance/PayableAuditTrailTests.cs`

**Interfaces:**
- Consumes: `PayableAuditTrailBuilder.Build(...)` (Task 4), `IUserDirectory.GetNamesAsync(...)` (Task 1), `AuditTrailEventResponse` (Task 4).
- Produces: endpoint returning `IReadOnlyList<AuditTrailEventResponse>`; route `GET {id}/audit-trail` under `/payables`. Adds `AttachmentUploaded` / `AttachmentDeleted` event types.

- [ ] **Step 1: Write the failing integration test**

Create `tests/Ulbra.One.IntegrationTests/Finance/PayableAuditTrailTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableAuditTrailTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AuditTrail_returns_created_classified_approved_for_a_scheduled_payable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor A");
        var (payableId, _) = await FinanceSeed.CreatePayableAsync(client, companyId, vendor, cc, today.AddDays(10), 100m, approve: true);

        var res = await client.GetAsync($"/api/payables/{payableId}/audit-trail");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var types = body.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Equal(new[] { "Created", "Classified", "Approved" }, types);

        // Actor name resolved to the admin display name (not a raw guid).
        Assert.Equal("Admin Teste", body[0].GetProperty("actorName").GetString());
        // Timestamps are ascending.
        var t0 = body[0].GetProperty("timestamp").GetDateTimeOffset();
        var t2 = body[2].GetProperty("timestamp").GetDateTimeOffset();
        Assert.True(t0 <= t2);
    }

    [Fact]
    public async Task AuditTrail_returns_404_for_unknown_payable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var res = await client.GetAsync($"/api/payables/{Guid.NewGuid()}/audit-trail");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
```

(If `CreatePayableAsync(approve: true)` does not produce a `Classified` event because it approves without an explicit classify call, adjust the seed helper so an approved payable is classified first — approval requires classification under the default policy, so `IsClassified` will be true and the `Classified` event will appear.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~PayableAuditTrailTests`
Expected: FAIL — 404 (route not mapped) for the first test.

- [ ] **Step 3: Add attachment events to the builder**

In `src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/PayableAuditTrailBuilder.cs`, extend `Build(...)` to take attachment events. Change the signature to accept the payable's attachments (already on the aggregate as `payable.Attachments`) and emit an `AttachmentUploaded` event per current attachment. Insert this loop **before** the final `OrderBy`:

```csharp
        foreach (var attachment in payable.Attachments)
        {
            var meta = new Dictionary<string, string> { ["fileName"] = attachment.FileName };
            events.Add(EventByGuid("AttachmentUploaded", attachment.UploadedById, attachment.CreatedAt, userNames, meta));
        }
```

Verify the exact property names on `PayableAttachment` (`FileName`, `UploadedById`, `CreatedAt`) by reading `src/Modules/Finance/Ulbra.One.Finance.Domain/Entities/PayableAttachment.cs`; if `UploadedById` is named differently (e.g. `CreatedById`), use the real name. (`AttachmentDeleted` is not reconstructable from current state — deleted attachments are gone — so it is intentionally omitted here; document this as a known limitation, to be sourced from AuditLog in a later iteration if needed.)

Add a unit test for this in `PayableAuditTrailBuilderTests.cs`:

```csharp
    [Fact]
    public void Build_emits_attachment_uploaded_events()
    {
        var uploader = Guid.NewGuid();
        var payable = PayableTestFactory.ReceivedWithAttachment(actorId: Guid.NewGuid().ToString(), createdAt: T0,
            uploaderId: uploader, fileName: "nf.xml", uploadedAt: T0.AddHours(1));
        var names = new Dictionary<Guid, string> { [uploader] = "Carlos" };

        var events = PayableAuditTrailBuilder.Build(payable, names, null, new Dictionary<Guid, DateTimeOffset>());

        var att = Assert.Single(events, e => e.Type == "AttachmentUploaded");
        Assert.Equal("nf.xml", att.Metadata["fileName"]);
        Assert.Equal("Carlos", att.ActorName);
    }
```

Add the `ReceivedWithAttachment` factory method to `PayableTestFactory` (build a received payable, then `AddAttachment(new PayableAttachment(...))` using the real `PayableAttachment` constructor). Run `dotnet test tests/Ulbra.One.UnitTests --filter FullyQualifiedName~PayableAuditTrailBuilderTests` and confirm PASS before wiring the endpoint.

- [ ] **Step 4: Create the endpoint**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Payables/AuditTrail/GetPayableAuditTrail.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AuditTrail;

public sealed class GetPayableAuditTrail : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("{id}/audit-trail", Handle).WithName("GetPayableAuditTrail").WithTags("Finance")
            .RequireAuthorization().Produces<IReadOnlyList<AuditTrailEventResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        PayableId id,
        IUnitOfWork uow,
        [FromKeyedServices("audit")] IUnitOfWork audit,
        IUserDirectory users,
        CancellationToken ct)
    {
        var payable = await uow.ReadSet<Payable>()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (payable is null)
            return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        // --- AuditLog-sourced timestamps (hybrid "C"): the two actions lacking an entity timestamp. ---
        DateTimeOffset? cancelledAt = null;
        if (payable.Status == PayableStatus.Cancelled)
        {
            var pk = payable.Id.ToString();
            cancelledAt = await audit.ReadSet<AuditLog>()
                .Where(a => a.EntityType == "Payable" && a.PrimaryKey == pk
                            && a.Action == AuditAction.Update && a.AffectedColumns != null
                            && a.AffectedColumns.Contains("CancelReason"))
                .OrderByDescending(a => a.OccurredAt)
                .Select(a => (DateTimeOffset?)a.OccurredAt)
                .FirstOrDefaultAsync(ct);
        }

        var reversedPaymentIds = payable.Installments
            .SelectMany(i => i.Payments).Where(p => p.IsReversed).Select(p => p.Id.ToString()).ToList();
        var paymentReversalTimes = new Dictionary<Guid, DateTimeOffset>();
        if (reversedPaymentIds.Count > 0)
        {
            var rows = await audit.ReadSet<AuditLog>()
                .Where(a => a.EntityType == "Payment" && reversedPaymentIds.Contains(a.PrimaryKey)
                            && a.Action == AuditAction.Update && a.AffectedColumns != null
                            && a.AffectedColumns.Contains("IsReversed"))
                .Select(a => new { a.PrimaryKey, a.OccurredAt })
                .ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.PrimaryKey))
                if (Guid.TryParse(g.Key, out var pid))
                    paymentReversalTimes[pid] = g.Max(x => x.OccurredAt);
        }

        // --- Resolve actor names for every Guid actor referenced. ---
        var guidActors = new List<Guid>();
        foreach (var i in payable.Installments)
            foreach (var p in i.Payments)
                guidActors.Add(p.CreatedById);
        foreach (var a in payable.Attachments)
            guidActors.Add(a.UploadedById);
        if (Guid.TryParse(payable.CreatedBy, out var cg)) guidActors.Add(cg);
        if (Guid.TryParse(payable.ApprovedBy, out var ag)) guidActors.Add(ag);
        if (Guid.TryParse(payable.RejectedBy, out var rg)) guidActors.Add(rg);
        if (Guid.TryParse(payable.UpdatedBy, out var ug)) guidActors.Add(ug);

        var names = await users.GetNamesAsync(guidActors, ct);

        var events = PayableAuditTrailBuilder.Build(payable, names, cancelledAt, paymentReversalTimes);
        return TypedResults.Ok(events);
    }
}
```

(Verify `using Ulbra.One.Audit.Domain.Entities;` is the correct namespace for `AuditLog` and `AuditAction`. If `AffectedColumns.Contains(...)` does not translate in EF for the `jsonb` column, fall back to filtering only by `EntityType`/`PrimaryKey`/`Action == Update` and take the latest `OccurredAt` — the test still passes because the timestamp only needs to be plausible and ordered.)

- [ ] **Step 5: Register the endpoint**

In `FinanceEndpoints.cs`, on the `/payables` group:

```csharp
        payables.MapEndpoint<GetPayableAuditTrail>();
```

Add `using Ulbra.One.Finance.Application.Endpoints.Payables.AuditTrail;`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~PayableAuditTrailTests`
Expected: PASS (2 tests). Also re-run the unit suite: `dotnet test tests/Ulbra.One.UnitTests --filter FullyQualifiedName~PayableAuditTrailBuilderTests` — PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/PayableAuditTrailBuilder.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Payables/AuditTrail/GetPayableAuditTrail.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs \
        tests/Ulbra.One.IntegrationTests/Finance/PayableAuditTrailTests.cs \
        tests/Ulbra.One.UnitTests/Finance/PayableAuditTrailBuilderTests.cs \
        tests/Ulbra.One.UnitTests/Finance/PayableTestFactory.cs \
        tests/Ulbra.One.IntegrationTests/Finance/FinanceSeed.cs
git commit -m "feat(AEL-621): payable audit-trail endpoint (hybrid source + IUserDirectory)"
```

---

### Task 6: Receivable audit-trail (builder + endpoint)

`GET /api/receivables/{id}/audit-trail`. Receivable has cleaner audit fields — `Cancel` and receipt reversals both carry timestamps + actor Guids — so no AuditLog fallback is needed here. Event types: `Created`, `Cancelled`, `WrittenOff`, `ReceiptRegistered`, `ReceiptReversed`.

**Files:**
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/ReceivableAuditTrailBuilder.cs`
- Create: `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Receivables/AuditTrail/GetReceivableAuditTrail.cs`
- Modify: `src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs`
- Test: `tests/Ulbra.One.UnitTests/Finance/ReceivableAuditTrailBuilderTests.cs`
- Test: `tests/Ulbra.One.IntegrationTests/Finance/ReceivableAuditTrailTests.cs`

**Interfaces:**
- Consumes: `AuditTrailEventResponse` (Task 4), `IUserDirectory` (Task 1).
- Produces:
  - `public static class ReceivableAuditTrailBuilder { public static IReadOnlyList<AuditTrailEventResponse> Build(Receivable receivable, IReadOnlyDictionary<Guid, string> userNames); }`
  - Endpoint `GetReceivableAuditTrail : IEndpoint`, route `GET {id}/audit-trail` under `/receivables`.

- [ ] **Step 1: Write the failing unit test**

Create `tests/Ulbra.One.UnitTests/Finance/ReceivableAuditTrailBuilderTests.cs`:

```csharp
using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class ReceivableAuditTrailBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_emits_created_then_cancelled_with_actor_and_reason()
    {
        var actor = Guid.NewGuid();
        var receivable = ReceivableTestFactory.Cancelled(
            createdActorId: "system", createdAt: T0, cancelledById: actor, cancelledAt: T0.AddDays(1), reason: "acordo");
        var names = new Dictionary<Guid, string> { [actor] = "Ana" };

        var events = ReceivableAuditTrailBuilder.Build(receivable, names);

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e =>
            {
                Assert.Equal("Cancelled", e.Type);
                Assert.Equal("Ana", e.ActorName);
                Assert.Equal("acordo", e.Metadata["reason"]);
                Assert.Equal(T0.AddDays(1), e.Timestamp);
            });
    }
}
```

Create `tests/Ulbra.One.UnitTests/Finance/ReceivableTestFactory.cs` modeled on the receivable construction in existing unit tests (read `tests/Ulbra.One.UnitTests/Finance/` for a receivable example, or the `Receivable` constructor + `MarkCreated`/`Cancel(cancelledById, at, reason)` signatures in `Receivable.cs`). Provide at least `Cancelled(string createdActorId, DateTimeOffset createdAt, Guid cancelledById, DateTimeOffset cancelledAt, string reason)`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Ulbra.One.UnitTests --filter FullyQualifiedName~ReceivableAuditTrailBuilderTests`
Expected: FAIL to compile — `ReceivableAuditTrailBuilder` does not exist.

- [ ] **Step 3: Create the builder**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/ReceivableAuditTrailBuilder.cs`:

```csharp
using System.Globalization;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.AuditTrail;

/// <summary>Synthesizes a receivable's typed audit timeline from its explicit audit fields.</summary>
public static class ReceivableAuditTrailBuilder
{
    private const string SystemActorName = "Sistema";

    public static IReadOnlyList<AuditTrailEventResponse> Build(
        Receivable receivable, IReadOnlyDictionary<Guid, string> userNames)
    {
        var events = new List<AuditTrailEventResponse>
        {
            FromString("Created", receivable.CreatedBy, receivable.CreatedAt, userNames, new Dictionary<string, string>()),
        };

        if (receivable.Status == ReceivableStatus.Cancelled && receivable.CancelledAt is { } cancelledAt)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(receivable.CancelReason)) meta["reason"] = receivable.CancelReason;
            events.Add(FromGuid("Cancelled", receivable.CancelledById, cancelledAt, userNames, meta));
        }

        if (receivable.Status == ReceivableStatus.WrittenOff)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(receivable.WriteOffReason)) meta["reason"] = receivable.WriteOffReason;
            // WriteOff carries no timestamp/actor on the entity; use UpdatedAt as the marker.
            events.Add(FromString("WrittenOff", receivable.UpdatedBy, receivable.UpdatedAt, userNames, meta));
        }

        foreach (var installment in receivable.Installments)
        {
            foreach (var receipt in installment.Receipts)
            {
                var regMeta = new Dictionary<string, string>
                {
                    ["installment"] = installment.Sequence.ToString(CultureInfo.InvariantCulture),
                    ["amount"] = receipt.Amount.ToString(CultureInfo.InvariantCulture),
                };
                events.Add(FromGuid("ReceiptRegistered", receipt.CreatedById, receipt.CreatedAt, userNames, regMeta));

                if (receipt.IsReversed && receipt.ReversedAt is { } reversedAt)
                {
                    var revMeta = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(receipt.ReversalReason)) revMeta["reason"] = receipt.ReversalReason;
                    var actor = receipt.ReversedById ?? receipt.CreatedById;
                    events.Add(FromGuid("ReceiptReversed", actor, reversedAt, userNames, revMeta));
                }
            }
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    private static AuditTrailEventResponse FromString(
        string type, string? actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> meta)
    {
        string actorName = SystemActorName;
        if (!string.IsNullOrEmpty(actorId))
            actorName = Guid.TryParse(actorId, out var g) && names.TryGetValue(g, out var n) ? n : actorId;
        return new AuditTrailEventResponse(type, string.IsNullOrEmpty(actorId) ? null : actorId, actorName, at, meta);
    }

    private static AuditTrailEventResponse FromGuid(
        string type, Guid actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> meta)
    {
        var actorName = names.TryGetValue(actorId, out var n) ? n : SystemActorName;
        return new AuditTrailEventResponse(type, actorId.ToString(), actorName, at, meta);
    }
}
```

- [ ] **Step 4: Run the unit test to verify it passes**

Run: `dotnet test tests/Ulbra.One.UnitTests --filter FullyQualifiedName~ReceivableAuditTrailBuilderTests`
Expected: PASS.

- [ ] **Step 5: Write the failing integration test**

Create `tests/Ulbra.One.IntegrationTests/Finance/ReceivableAuditTrailTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ReceivableAuditTrailTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AuditTrail_returns_created_then_receipt_registered()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var customer = await FinanceSeed.CreateCustomerAsync(client, companyId, "Cliente A");
        var bank = await FinanceSeed.CreateBankAccountAsync(client, companyId, "00012345");
        var (rcv, inst) = await FinanceSeed.CreateReceivableAsync(client, companyId, customer, cc, today.AddDays(5), 100m);
        await FinanceSeed.RegisterReceiptAsync(client, rcv, inst, bank, 100m, today);

        var res = await client.GetAsync($"/api/receivables/{rcv}/audit-trail");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var types = body.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Equal("Created", types[0]);
        Assert.Contains("ReceiptRegistered", types);
    }

    [Fact]
    public async Task AuditTrail_returns_404_for_unknown_receivable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var res = await client.GetAsync($"/api/receivables/{Guid.NewGuid()}/audit-trail");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~ReceivableAuditTrailTests`
Expected: FAIL — 404 (route not mapped).

- [ ] **Step 7: Create the endpoint**

Create `src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Receivables/AuditTrail/GetReceivableAuditTrail.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.AuditTrail;

public sealed class GetReceivableAuditTrail : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("{id}/audit-trail", Handle).WithName("GetReceivableAuditTrail").WithTags("Finance")
            .RequireAuthorization().Produces<IReadOnlyList<AuditTrailEventResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        ReceivableId id, IUnitOfWork uow, IUserDirectory users, CancellationToken ct)
    {
        var receivable = await uow.ReadSet<Receivable>()
            .Include(x => x.Installments)
            .FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (receivable is null)
            return TypedResults.Problem(detail: "Receivable not found.", statusCode: 404, title: "Receivable.NotFound");

        var guidActors = new List<Guid>();
        if (receivable.CancelledById is { } cb) guidActors.Add(cb);
        foreach (var i in receivable.Installments)
            foreach (var r in i.Receipts)
            {
                guidActors.Add(r.CreatedById);
                if (r.ReversedById is { } rb) guidActors.Add(rb);
            }
        if (Guid.TryParse(receivable.CreatedBy, out var cg)) guidActors.Add(cg);
        if (Guid.TryParse(receivable.UpdatedBy, out var ug)) guidActors.Add(ug);

        var names = await users.GetNamesAsync(guidActors, ct);
        var events = ReceivableAuditTrailBuilder.Build(receivable, names);
        return TypedResults.Ok(events);
    }
}
```

(`Receipts` are auto-included under installments, so `.Include(x => x.Installments)` loads receipts too — matching `GetReceivable.cs`.)

- [ ] **Step 8: Register the endpoint**

In `FinanceEndpoints.cs`, on the `/receivables` group:

```csharp
        receivables.MapEndpoint<GetReceivableAuditTrail>();
```

Add `using Ulbra.One.Finance.Application.Endpoints.Receivables.AuditTrail;`.

- [ ] **Step 9: Run the integration tests to verify they pass**

Run: `dotnet test tests/Ulbra.One.IntegrationTests --filter FullyQualifiedName~ReceivableAuditTrailTests`
Expected: PASS (2 tests).

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Finance/Ulbra.One.Finance.Application/AuditTrail/ReceivableAuditTrailBuilder.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/Endpoints/Receivables/AuditTrail/GetReceivableAuditTrail.cs \
        src/Modules/Finance/Ulbra.One.Finance.Application/FinanceEndpoints.cs \
        tests/Ulbra.One.UnitTests/Finance/ReceivableAuditTrailBuilderTests.cs \
        tests/Ulbra.One.UnitTests/Finance/ReceivableTestFactory.cs \
        tests/Ulbra.One.IntegrationTests/Finance/ReceivableAuditTrailTests.cs
git commit -m "feat(AEL-621): receivable audit-trail endpoint"
```

---

### Task 7: Regenerate the OpenAPI client + full verification

Export the updated swagger to `web/openapi.json`, regenerate the TS schema, and confirm the whole solution builds + all tests pass.

**Files:**
- Modify: `web/openapi.json` (regenerated)
- Modify: `web/src/api/schema.d.ts` (regenerated)

**Interfaces:**
- Produces: updated `web/src/api/schema.d.ts` exposing `PayableStageSummaryResponse`, `ReceivableStageSummaryResponse`, `StageBucket`, `AuditTrailEventResponse` and the four new paths — so plans 3–5 (the FE pages) can consume them typed.

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeds, 0 errors.

- [ ] **Step 2: Run the full backend test suite**

Run: `dotnet test`
Expected: All tests pass (the new unit + integration tests plus the pre-existing suite).

- [ ] **Step 3: Start the API and export swagger**

In one shell, run the API:
```bash
dotnet run --project src/API/Ulbra.One.Api
```
Wait for it to listen (note the HTTP port — typically `http://localhost:5001`; check the console output). In a second shell, export the spec (use the port the API actually bound to):
```bash
curl -s http://localhost:5001/swagger/v1/swagger.json -o web/openapi.json
```
Then stop the API (Ctrl-C in the first shell). Confirm the file changed and contains the new operations:
```bash
grep -c "stage-summary\|audit-trail" web/openapi.json
```
Expected: a non-zero count (the four new operations are present).

- [ ] **Step 4: Regenerate the TS schema**

Run: `pnpm --dir web gen:api`
Expected: `web/src/api/schema.d.ts` regenerates with no errors.

- [ ] **Step 5: Typecheck the frontend**

Run: `pnpm --dir web typecheck`
Expected: PASS (the regenerated schema is valid; nothing consumes the new types yet, so no breakage).

- [ ] **Step 6: Commit**

```bash
git add web/openapi.json web/src/api/schema.d.ts
git commit -m "chore(AEL-621): regenerate openapi.json + schema.d.ts for stage-summary + audit-trail"
```

---

## Self-Review

**Spec coverage (this plan = spec §5 Backend Gaps):**
- §5.1 Payable stage-summary → Task 2 ✓
- §5.1 Receivable stage-summary (entry/receipt/reconciled, AwaitingReceipt split by receivedAmount) → Task 3 ✓
- §5.2 Payable audit-trail (typed events Created/Classified/Approved/Rejected/Cancelled/PaymentRegistered/PaymentReversed/AttachmentUploaded) → Tasks 4 + 5 ✓ (AttachmentDeleted intentionally omitted — not reconstructable from current state; documented in Task 5 Step 3)
- §5.2 Receivable audit-trail (Created/Cancelled/WrittenOff/ReceiptRegistered/ReceiptReversed) → Task 6 ✓
- Hybrid source "C" (entity fields primary; AuditLog for Payable.Cancel + Payment.Reverse timestamps) → Task 5 Step 4 ✓
- actorName via cross-module-safe resolution → Task 1 (`IUserDirectory`) ✓
- Regenerate openapi.json + schema.d.ts → Task 7 ✓
- §5.3 future gaps (approver filtering, aggregated inbox) are explicitly out of scope.

**Placeholder scan:** No "TBD"/"implement later". A few steps instruct the implementer to *verify an exact property/namespace name against a named source file* (e.g. `PayableAttachment.UploadedById`, the `IEndpoint`/`AuditLog` using lines, the FinanceEndpoints group variable). These are deliberate — the exploration could not capture every verbatim member, and each names the precise file to read and the fallback to use. They are not open-ended "handle edge cases" placeholders.

**Type consistency:** `StageBucket` defined in Task 2, reused in Task 3 (same namespace `Ulbra.One.Finance.Application.Contracts`). `AuditTrailEventResponse` defined in Task 4, reused in Tasks 5 + 6. `PayableAuditTrailBuilder.Build` signature is consistent between Task 4 (definition) and Task 5 (consumption) — Task 5 Step 3 extends the same method to read `payable.Attachments` (already on the aggregate, no signature change). `IUserDirectory.GetNamesAsync(IReadOnlyCollection<Guid>, CancellationToken)` defined in Task 1, consumed in Tasks 5 + 6. `FinanceSeed` helper is created in Task 2 and extended in Task 3 (customer/receivable/receipt helpers).

**Known cross-task dependency:** Tasks 2 and 3 both touch `FinanceSeed.cs`; Task 2 creates it, Task 3 extends it. Tasks 2, 3, 5, 6 all touch `FinanceEndpoints.cs` (additive registration lines, distinct groups) — low conflict risk under sequential execution.

---

## Decomposition Note

This is plan **2 of 5** of the overhaul (AEL-621 under epic AEL-620). It unblocks plans 3 (payables pages) and 4 (receivables pages), which consume `stage-summary` (for `PendencyDashboard`) and `audit-trail` (for `AuditTimeline`). Plan 5 (approvals inbox) depends only on plan 1. The FE query hooks (`usePayableStageSummary`, `usePayableAuditTrail`, …) are added in plans 3–5 against the schema regenerated here.
