using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Api;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Persistence;

namespace Ulbra.One.IntegrationTests;

/// <summary>
/// Guarantees for <see cref="DevShowcaseSeeder"/>, which runs on every Development startup — including the
/// Azure <c>dev</c> environment. Two things must hold, and only a real database can show them:
/// <list type="number">
///   <item>It covers every lifecycle state the screens can render (that is the whole point of it).</item>
///   <item>It is idempotent — a second run adds nothing. Anything else would grow the DEV database without
///     bound, one restart at a time.</item>
/// </list>
/// The suite host boots in Development, so the seeder has already run once by the time these tests execute.
/// </summary>
[Collection("Postgres")]
public class DevShowcaseSeederTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Seed_CoversEveryPayableStatus()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var status in Enum.GetValues<PayableStatus>())
            Assert.True(await db.Set<Payable>().AnyAsync(p => p.Status == status),
                $"No payable in status {status} — the payables showcase did not cover it.");
    }

    [Fact]
    public async Task Seed_CoversEveryReconciliationState()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var status in Enum.GetValues<MatchStatus>())
            Assert.True(await db.Set<BankTransaction>().AnyAsync(t => t.MatchStatus == status),
                $"No bank transaction in match status {status}.");

        // Both statement sources, so the imports list shows more than one shape of upload.
        foreach (var source in Enum.GetValues<StatementSource>())
            Assert.True(await db.Set<BankStatementImport>().AnyAsync(i => i.Source == source),
                $"No statement import from source {source}.");
    }

    [Fact]
    public async Task Seed_CoversEveryRemittanceAndReceivableState()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var status in Enum.GetValues<RemittanceStatus>())
            Assert.True(await db.Set<Remittance>().AnyAsync(r => r.Status == status),
                $"No remittance in status {status}.");

        foreach (var status in Enum.GetValues<RemittanceLineStatus>())
            Assert.True(await db.Set<RemittanceLine>().AnyAsync(l => l.Status == status),
                $"No remittance line in status {status}.");

        foreach (var status in Enum.GetValues<ReceivableStatus>())
            Assert.True(await db.Set<Receivable>().AnyAsync(r => r.Status == status),
                $"No receivable in status {status}.");

        Assert.True(await db.Set<RemittanceReturn>().AnyAsync(), "No processed remittance return.");
    }

    [Fact]
    public async Task Seed_CoversEveryAccountingAndBudgetingState()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var status in Enum.GetValues<JournalStatus>())
            Assert.True(await db.Set<JournalEntry>().AnyAsync(e => e.Status == status),
                $"No journal entry in status {status}.");

        foreach (var status in Enum.GetValues<ProjectStatus>())
            Assert.True(await db.Set<Project>().AnyAsync(p => p.Status == status),
                $"No project in status {status}.");

        foreach (var status in Enum.GetValues<BudgetAdjustmentStatus>())
            Assert.True(await db.Set<BudgetAdjustment>().AnyAsync(a => a.Status == status),
                $"No budget adjustment in status {status}.");

        Assert.True(await db.Set<PendingAccountingPosting>().AnyAsync(p => p.ResolvedAt == null),
            "No parked accounting posting.");
        Assert.True(await db.Set<AccountingPeriod>().AnyAsync(p => p.Status == AccountingPeriodStatus.Closed),
            "No closed accounting period.");
    }

    /// <summary>
    /// The reconciliation seed only earns its keep if the screen actually shows each verdict, and the verdict is
    /// computed by the list endpoint from the live installment pool — not stored. So this asks the endpoint,
    /// scoped to the seeded import, and requires every <see cref="ConciliationMismatch"/> case to be present.
    /// </summary>
    [Fact]
    public async Task Seed_Conciliation_ProducesEveryMismatchVerdict_OnTheListEndpoint()
    {
        Guid importId;
        Guid companyId;
        using (var scope = fixture.Api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var import = await db.Set<BankStatementImport>()
                .Where(i => i.Source == StatementSource.OfxUpload && i.FileName.StartsWith("showcase-"))
                .OrderBy(i => i.FileName)
                .FirstAsync();
            importId = import.Id;
            companyId = import.CompanyId;
        }

        var client = await fixture.Api.CreateAdminClientAsync();
        var res = await client.GetAsync(
            $"/api/bank-transactions?companyId={new CompanyId(companyId)}&importId={new StatementImportId(importId)}&pageSize=100");
        res.EnsureSuccessStatusCode();

        var page = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var verdicts = page.GetProperty("items").EnumerateArray()
            .Select(x => x.TryGetProperty("mismatch", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null)
            .Where(v => v is not null)
            .ToHashSet();

        // Every unmatched verdict the screen can explain, on one import.
        foreach (var expected in new[]
        {
            nameof(ConciliationMismatch.NoCandidate),
            nameof(ConciliationMismatch.AmountDiffers),
            nameof(ConciliationMismatch.OutsideDateWindow),
            nameof(ConciliationMismatch.Ambiguous),
            nameof(ConciliationMismatch.ExactAvailable),
            nameof(ConciliationMismatch.CreditNotMatched),
        })
            Assert.Contains(expected, verdicts);
    }

    /// <summary>
    /// The load-bearing test: the seeder runs on every Development startup, so a second run must be a no-op.
    /// Counts every table it writes to, runs it again, and requires the counts to be identical.
    /// </summary>
    [Fact]
    public async Task Seed_IsIdempotent_WhenRunTwice()
    {
        var before = await CountsAsync();

        await DevShowcaseSeeder.SeedAsync(fixture.Api.Services);

        var after = await CountsAsync();
        Assert.Equal(before, after);

        async Task<Dictionary<string, int>> CountsAsync()
        {
            using var scope = fixture.Api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return new Dictionary<string, int>
            {
                ["payables"] = await db.Set<Payable>().CountAsync(),
                ["installments"] = await db.Set<Installment>().CountAsync(),
                ["payments"] = await db.Set<Payment>().CountAsync(),
                ["statement_imports"] = await db.Set<BankStatementImport>().CountAsync(),
                ["bank_transactions"] = await db.Set<BankTransaction>().CountAsync(),
                ["remittances"] = await db.Set<Remittance>().CountAsync(),
                ["remittance_lines"] = await db.Set<RemittanceLine>().CountAsync(),
                ["remittance_returns"] = await db.Set<RemittanceReturn>().CountAsync(),
                ["receivables"] = await db.Set<Receivable>().CountAsync(),
                ["receipts"] = await db.Set<Receipt>().CountAsync(),
                ["journal_entries"] = await db.Set<JournalEntry>().CountAsync(),
                ["accounting_periods"] = await db.Set<AccountingPeriod>().CountAsync(),
                ["pending_postings"] = await db.Set<PendingAccountingPosting>().CountAsync(),
                ["projects"] = await db.Set<Project>().CountAsync(),
                ["budget_adjustments"] = await db.Set<BudgetAdjustment>().CountAsync(),
            };
        }
    }
}
