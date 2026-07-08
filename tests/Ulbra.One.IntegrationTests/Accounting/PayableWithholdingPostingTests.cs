using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.IntegrationTests.Accounting;

/// <summary>
/// Integration tests for the multi-line withholding incurral split:
/// Dr despesa (bruto) / Cr fornecedores (líquido) / Cr impostos (Σ retenções).
/// Driven by calling <see cref="IPayablePostingService.PostIncurralAsync"/> directly
/// (broker-free) against the real seeded RFB Imunes e Isentas chart.
/// </summary>
[Collection("Postgres")]
public class PayableWithholdingPostingTests(PostgresFixture fixture)
{
    // RFB Imunes e Isentas codes from AccountingModule defaults (🔶 contábil-validate):
    //   4.02.02.01.05 = Despesas de Consumo  (expense — Dr side)
    //   2.01.01.03.01 = Fornecedores         (payables liability — Cr side)
    //   2.01.01.09.01 = Tributos Federais a Recolher (IRRF/PIS/COFINS/CSLL)
    //   2.01.01.01.02 = INSS a Recolher
    //   2.01.01.09.03 = Tributos Municipais a Recolher (ISS)

    private const string ExpenseCode = "4.02.02.01.05";
    private const string LiabilityCode = "2.01.01.03.01";
    private const string IrrfCode = "2.01.01.09.01";

    private static async Task<Guid> SeedCompanyWithRfbChartAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Retenção Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var prefixedId = JsonDocument.Parse(await companyRes.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetString()!;
        var companyId = CompanyId.Parse(prefixedId, null).Value;

        var seedRes = await client.PostAsJsonAsync("/api/ledger-accounts/seed",
            new { companyId = prefixedId, template = "RfbImunesIsentas" });
        seedRes.EnsureSuccessStatusCode();

        return companyId;
    }

    private static async Task<Guid> AccountIdAsync(IUnitOfWork uow, string code) =>
        (await uow.ReadSet<LedgerAccount>().FirstAsync(a => a.Code == code)).Id;

    /// <summary>
    /// Case 1 — Split: gross 1000, IRRF 150 → 3-line entry:
    ///   Dr 4.02.02.01.05 = 1000 (expense, gross)
    ///   Cr 2.01.01.03.01 = 850  (fornecedores, net)
    ///   Cr 2.01.01.09.01 = 150  (IRRF a recolher)
    /// Balanced, Posted, no PendingAccountingPosting.
    /// </summary>
    [Fact]
    public async Task PostIncurral_WithIrrfWithholding_CreatesThreeLineSplitEntry()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyWithRfbChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var withholdings = new List<(string TaxType, decimal Amount)> { ("Irrf", 150m) };

        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        await posting.PostIncurralAsync(companyId, sourceKey, 1000m, expenseId, withholdings, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // No pending posting.
        var hasPending = await uow.ReadSet<PendingAccountingPosting>()
            .AnyAsync(p => p.CompanyId == companyId && p.SourceKey == sourceKey);
        Assert.False(hasPending);

        // A Posted JournalEntry must exist.
        var entry = await uow.ReadSet<JournalEntry>()
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey);
        Assert.NotNull(entry);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit); // balanced

        // Resolve account ids by code for assertion.
        var accountIds = await uow.ReadSet<LedgerAccount>()
            .Where(a => a.Code == ExpenseCode || a.Code == LiabilityCode || a.Code == IrrfCode)
            .ToDictionaryAsync(a => a.Code, a => a.Id);

        Assert.Equal(3, entry.Lines.Count);

        var drLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit);
        Assert.Equal(accountIds[ExpenseCode], drLine.LedgerAccountId);
        Assert.Equal(1000m, drLine.Amount);

        var crLines = entry.Lines.Where(l => l.Side == EntrySide.Credit).ToList();
        Assert.Equal(2, crLines.Count);

        var crLiability = Assert.Single(crLines, l => l.LedgerAccountId == accountIds[LiabilityCode]);
        Assert.Equal(850m, crLiability.Amount); // net = 1000 - 150

        var crIrrf = Assert.Single(crLines, l => l.LedgerAccountId == accountIds[IrrfCode]);
        Assert.Equal(150m, crIrrf.Amount);
    }

    /// <summary>
    /// Case 2 — Unresolvable: withholding of type "Other" has no mapped code in defaults
    /// → PendingAccountingPosting is created, no JournalEntry.
    /// </summary>
    [Fact]
    public async Task PostIncurral_WithOtherWithholding_CreatesPendingPosting_NoEntry()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyWithRfbChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // "Other" is intentionally NOT in WithholdingCodes → unresolvable.
        var withholdings = new List<(string TaxType, decimal Amount)> { ("Other", 50m) };

        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        await posting.PostIncurralAsync(companyId, sourceKey, 1000m, expenseId, withholdings, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // No journal entry.
        var hasEntry = await uow.ReadSet<JournalEntry>()
            .AnyAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey);
        Assert.False(hasEntry);

        // One PendingAccountingPosting.
        var pendings = await uow.ReadSet<PendingAccountingPosting>()
            .Where(p => p.CompanyId == companyId && p.SourceKey == sourceKey)
            .ToListAsync();
        Assert.Single(pendings);
        Assert.Equal(PostingKind.Incurral, pendings[0].Kind);
        Assert.Equal(1000m, pendings[0].Amount);
    }

    /// <summary>
    /// Case 3 — Regression: no withholdings → exactly the classic 2-line entry
    /// (Dr expense / Cr fornecedores, both = gross), balanced, Posted.
    /// </summary>
    [Fact]
    public async Task PostIncurral_NoWithholdings_CreatesClassicTwoLineEntry()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyWithRfbChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        await posting.PostIncurralAsync(companyId, sourceKey, 1000m, expenseId, [], CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var entry = await uow.ReadSet<JournalEntry>()
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey);
        Assert.NotNull(entry);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit); // balanced
        Assert.Equal(2, entry.Lines.Count);

        var accountIds = await uow.ReadSet<LedgerAccount>()
            .Where(a => a.Code == ExpenseCode || a.Code == LiabilityCode)
            .ToDictionaryAsync(a => a.Code, a => a.Id);

        var drLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit);
        var crLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit);

        Assert.Equal(accountIds[ExpenseCode], drLine.LedgerAccountId);
        Assert.Equal(accountIds[LiabilityCode], crLine.LedgerAccountId);
        Assert.Equal(1000m, drLine.Amount);
        Assert.Equal(1000m, crLine.Amount);

        var hasPending = await uow.ReadSet<PendingAccountingPosting>()
            .AnyAsync(p => p.CompanyId == companyId && p.SourceKey == sourceKey);
        Assert.False(hasPending);
    }
}
