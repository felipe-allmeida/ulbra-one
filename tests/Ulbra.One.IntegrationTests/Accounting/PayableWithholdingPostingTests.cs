using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Accounting;

/// <summary>
/// Integration tests for the multi-line withholding incurral split:
/// Dr despesa (bruto) / Cr fornecedores (líquido) / Cr impostos (Σ retenções).
/// Driven by calling <see cref="IPayablePostingService.PostIncurralAsync"/> directly
/// (broker-free) against the real seeded AELBRA chart.
/// </summary>
[Collection("Postgres")]
public class PayableWithholdingPostingTests(PostgresFixture fixture)
{
    // AELBRA "Modelo de Planos" codes from AccountingModule defaults (🔶 contábil-validate):
    //   4.2.15.05.020 = Energia Elétrica       (expense — Dr side)
    //   2.2.05.15.080 = Fornecedores a Pagar   (payables liability — Cr side)
    //   2.2.05.30.020 = IRRF Retenção          (Cr side)
    //   2.2.05.30.080 = INSS Retenção
    //   2.2.05.30.060 = ISS Retenção

    private const string ExpenseCode = "4.2.15.05.020";
    private const string LiabilityCode = "2.2.05.15.080";
    private const string IrrfCode = "2.2.05.30.020";
    private const string CsrfCode = "2.2.05.30.040"; // PIS/COFINS/CSLL Retenção
    private const string CashCode = "1.2.05.15.015";  // Banco (settlement cash default)

    private static async Task<Guid> SeedCompanyAndChartAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Retenção Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var prefixedId = JsonDocument.Parse(await companyRes.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetString()!;
        var companyId = CompanyId.Parse(prefixedId, null).Value;

        var seedRes = await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { });
        seedRes.EnsureSuccessStatusCode();

        return companyId;
    }

    private static async Task<Guid> AccountIdAsync(IUnitOfWork uow, string code) =>
        (await uow.ReadSet<LedgerAccount>().FirstAsync(a => a.Code == code)).Id;

    /// <summary>
    /// Case 1 — Split: gross 1000, IRRF 150 → 3-line entry:
    ///   Dr 4.2.15.05.020 = 1000 (expense, gross)
    ///   Cr 2.2.05.15.080 = 850  (fornecedores, net)
    ///   Cr 2.2.05.30.020 = 150  (IRRF retenção)
    /// Balanced, Posted, no PendingAccountingPosting.
    /// </summary>
    [Fact]
    public async Task PostIncurral_WithIrrfWithholding_CreatesThreeLineSplitEntry()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var withholdings = new List<(string TaxType, decimal Amount)> { ("Irrf", 150m) };

        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        await posting.PostIncurralAsync(companyId, sourceKey, 1000m, expenseId, withholdings, competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
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
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // "Other" is intentionally NOT in WithholdingCodes → unresolvable.
        var withholdings = new List<(string TaxType, decimal Amount)> { ("Other", 50m) };

        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        await posting.PostIncurralAsync(companyId, sourceKey, 1000m, expenseId, withholdings, competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
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
    /// Case 2b — CSRF at settlement (baixa) against the real chart: gross paid 95, CSRF 4.65 →
    ///   Dr 2.2.05.15.080 = 99.65 (fornecedores, amount + Σ CSRF)
    ///   Cr 2.2.05.30.040 = 4.65  (PIS+COFINS+CSLL collapse to one account)
    ///   Cr 1.2.05.15.015 = 95    (cash paid)
    /// Balanced, Posted, no PendingAccountingPosting (proves 2.2.05.30.040 resolves in the seeded chart).
    /// </summary>
    [Fact]
    public async Task PostSettlement_WithCsrf_DebitsLiabilityPlusCsrf_CreditsCsrfAndCash()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var sourceKey = $"payment:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var withholdings = new List<(string TaxType, decimal Amount)> { ("Pis", 0.65m), ("Cofins", 3m), ("Csll", 1m) };
        await posting.PostSettlementAsync(companyId, sourceKey, 95m, new DateOnly(2026, 7, 1), cashAccountId: null, withholdings, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var hasPending = await uow.ReadSet<PendingAccountingPosting>()
            .AnyAsync(p => p.CompanyId == companyId && p.SourceKey == sourceKey);
        Assert.False(hasPending);

        var entry = await uow.ReadSet<JournalEntry>()
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey);
        Assert.NotNull(entry);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit); // balanced

        var accountIds = await uow.ReadSet<LedgerAccount>()
            .Where(a => a.Code == LiabilityCode || a.Code == CsrfCode || a.Code == CashCode)
            .ToDictionaryAsync(a => a.Code, a => a.Id);

        var drLine = Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit);
        Assert.Equal(accountIds[LiabilityCode], drLine.LedgerAccountId);
        Assert.Equal(99.65m, drLine.Amount); // 95 + 4.65

        var crLines = entry.Lines.Where(l => l.Side == EntrySide.Credit).ToList();
        Assert.Equal(2, crLines.Count); // PIS/COFINS/CSLL collapse into one summed line + cash
        Assert.Equal(4.65m, Assert.Single(crLines, l => l.LedgerAccountId == accountIds[CsrfCode]).Amount);
        Assert.Equal(95m, Assert.Single(crLines, l => l.LedgerAccountId == accountIds[CashCode]).Amount);
    }

    /// <summary>
    /// Case 3 — Regression: no withholdings → exactly the classic 2-line entry
    /// (Dr expense / Cr fornecedores, both = gross), balanced, Posted.
    /// </summary>
    [Fact]
    public async Task PostIncurral_NoWithholdings_CreatesClassicTwoLineEntry()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        await posting.PostIncurralAsync(companyId, sourceKey, 1000m, expenseId, [], competenceDate: null, costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
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

    /// <summary>
    /// End-to-end proof for the unit-on-retention fix: an incurral (IRRF, on issue) plus a settlement (CSRF, at
    /// payment), both tagged with a unit, must surface BOTH retention accounts in the balancete filtered by that
    /// unit — read through the real GET /trial-balance endpoint — and the filtered balance must close
    /// (Σdebit = Σcredit). Before the fix the retention/liability/cash lines carried a null unit and vanished
    /// from the unit-filtered trial balance.
    /// </summary>
    [Fact]
    public async Task TrialBalance_FilteredByUnit_SurfacesRetentionAccounts_AndBalances()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var unit = Guid.NewGuid();
        var payableId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var expenseId = await AccountIdAsync(uow, ExpenseCode);

        // Incurral: gross 1000, IRRF 150 → Dr expense 1000 / Cr fornecedores 850 / Cr IRRF 150 — all tagged with the unit.
        // Saved before the settlement (as separate consumers do in production) so each entry gets its own sequential Number.
        await posting.PostIncurralAsync(companyId, $"payable-incurral:{payableId}", 1000m, expenseId,
            [("Irrf", 150m)], competenceDate: new DateOnly(2026, 7, 10), costCenterId: null, unitId: unit,
            documentRef: "NF-UNIT", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Settlement: cash 850, CSRF (PIS/COFINS/CSLL) 46.50 → Dr fornecedores 896.50 / Cr CSRF 46.50 / Cr cash 850 — same unit.
        await posting.PostSettlementAsync(companyId, $"payment:{payableId}", 850m, new DateOnly(2026, 7, 20),
            cashAccountId: null, withholdings: [("Pis", 5.20m), ("Cofins", 24m), ("Csll", 17.30m)], unitId: unit,
            documentRef: "NF-UNIT", counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Read the balancete through the real endpoint, filtered by the unit (typed ids format their own query value).
        var companyParam = new CompanyId(companyId);
        var unitParam = new UnitId(unit);
        var res = await client.GetAsync($"/api/accounting/trial-balance?companyId={companyParam}&unitId={unitParam}");
        res.EnsureSuccessStatusCode();
        var tb = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        Assert.True(tb.GetProperty("isBalanced").GetBoolean()); // the per-unit balancete closes now
        var rows = tb.GetProperty("rows").EnumerateArray().ToList();
        JsonElement Row(string code) => rows.First(r => r.GetProperty("accountCode").GetString() == code);

        // Both retention accounts surface under this unit — the crux of the bug.
        Assert.Contains(rows, r => r.GetProperty("accountCode").GetString() == IrrfCode);
        Assert.Contains(rows, r => r.GetProperty("accountCode").GetString() == CsrfCode);
        Assert.Equal(150m, Row(IrrfCode).GetProperty("totalCredit").GetDecimal());
        Assert.Equal(46.50m, Row(CsrfCode).GetProperty("totalCredit").GetDecimal());

        // Control: a DIFFERENT unit sees none of this company's movement (the tagging is unit-scoped, not global).
        var otherUnit = new UnitId(Guid.NewGuid());
        var resOther = await client.GetAsync($"/api/accounting/trial-balance?companyId={companyParam}&unitId={otherUnit}");
        resOther.EnsureSuccessStatusCode();
        var tbOther = JsonDocument.Parse(await resOther.Content.ReadAsStringAsync()).RootElement;
        Assert.Empty(tbOther.GetProperty("rows").EnumerateArray());
    }
}
