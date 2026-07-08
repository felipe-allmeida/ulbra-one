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
/// Integration tests for <see cref="IPayablePostingService"/> wired against the real seeded AELBRA
/// chart. Broker-free: the service is resolved from DI and driven directly.
/// </summary>
[Collection("Postgres")]
public class PayableAccountingPostingTests(PostgresFixture fixture)
{
    // AELBRA "Modelo de Planos" codes used by AccountingModule defaults (analytical leaves):
    //   2.2.05.15.020 = Fornecedores de Funcionamento          (Liability, Analytical)
    //   1.2.05.15.015 = Bco Banrisul CC 060289920-4            (Asset, Analytical)
    // Expense code (also used in the dev journal-entry seed):
    //   4.2.15.05.020 = Energia Elétrica
    private const string ExpenseCode = "4.2.15.05.020";
    private const string LiabilityCode = "2.2.05.15.020";
    private const string CashCode = "1.2.05.15.015";

    private static async Task<Guid> SeedCompanyAndChartAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "Empresa Teste Contábil", cnpj });
        companyRes.EnsureSuccessStatusCode();
        // The API returns prefixed IDs (e.g. "company_<guid:N>"); parse the raw Guid from it.
        var prefixedId = JsonDocument.Parse(await companyRes.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetString()!;
        var companyId = CompanyId.Parse(prefixedId, null).Value;

        // Seed the AELBRA chart so config-default codes resolve.
        var seedRes = await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { });
        seedRes.EnsureSuccessStatusCode();

        return companyId;
    }

    private static async Task<Guid> AccountIdAsync(IUnitOfWork uow, string code) =>
        (await uow.ReadSet<LedgerAccount>().FirstAsync(a => a.Code == code)).Id;

    [Fact]
    public async Task PostIncurral_CreatesPostedJournalEntry_BalancedDrExpenseCrLiability()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        await posting.PostIncurralAsync(companyId, sourceKey, 100m, expenseId, [], CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var entry = await uow.ReadSet<JournalEntry>()
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey);

        Assert.NotNull(entry);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit); // balanced

        // Find accounts by code to assert sides.
        var accounts = await uow.ReadSet<LedgerAccount>()
            .Where(a => a.Code == ExpenseCode || a.Code == LiabilityCode)
            .ToDictionaryAsync(a => a.Code, a => a.Id);

        var lines = entry.Lines;
        var debitLine = lines.FirstOrDefault(l => l.Side == EntrySide.Debit);
        var creditLine = lines.FirstOrDefault(l => l.Side == EntrySide.Credit);

        Assert.NotNull(debitLine);
        Assert.NotNull(creditLine);
        Assert.Equal(accounts[ExpenseCode], debitLine.LedgerAccountId);
        Assert.Equal(accounts[LiabilityCode], creditLine.LedgerAccountId);
        Assert.Equal(100m, debitLine.Amount);
        Assert.Equal(100m, creditLine.Amount);

        // No pending posting was created.
        var pending = await uow.ReadSet<PendingAccountingPosting>()
            .AnyAsync(p => p.CompanyId == companyId && p.SourceKey == sourceKey);
        Assert.False(pending);
    }

    [Fact]
    public async Task PostSettlement_CreatesPostedJournalEntry_DrLiabilityCrCash()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var sourceKey = $"payment:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await posting.PostSettlementAsync(companyId, sourceKey, 50m, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Settlement resolved → a real entry must exist (not pending).
        // This also validates that both 2.2.05.15.020 (Fornecedores) and 1.2.05.15.015 (Bancos) exist in the chart.
        var entry = await uow.ReadSet<JournalEntry>()
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey);

        Assert.NotNull(entry);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit);

        var accounts = await uow.ReadSet<LedgerAccount>()
            .Where(a => a.Code == LiabilityCode || a.Code == CashCode)
            .ToDictionaryAsync(a => a.Code, a => a.Id);

        var lines = entry.Lines;
        var debitLine = lines.FirstOrDefault(l => l.Side == EntrySide.Debit);
        var creditLine = lines.FirstOrDefault(l => l.Side == EntrySide.Credit);

        Assert.NotNull(debitLine);
        Assert.NotNull(creditLine);
        Assert.Equal(accounts[LiabilityCode], debitLine.LedgerAccountId);
        Assert.Equal(accounts[CashCode], creditLine.LedgerAccountId);
        Assert.Equal(50m, debitLine.Amount);
        Assert.Equal(50m, creditLine.Amount);

        var pending = await uow.ReadSet<PendingAccountingPosting>()
            .AnyAsync(p => p.CompanyId == companyId && p.SourceKey == sourceKey);
        Assert.False(pending);
    }

    [Fact]
    public async Task PostIncurral_UnknownExpenseCode_CreatesPendingPosting_NoEntry()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";
        var unknownAccountId = Guid.NewGuid(); // not present in the chart → unresolvable

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await posting.PostIncurralAsync(companyId, sourceKey, 200m, unknownAccountId, [], CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // No journal entry created.
        var entry = await uow.ReadSet<JournalEntry>()
            .AnyAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey);
        Assert.False(entry);

        // One pending posting created.
        var pending = await uow.ReadSet<PendingAccountingPosting>()
            .Where(p => p.CompanyId == companyId && p.SourceKey == sourceKey)
            .ToListAsync();
        Assert.Single(pending);
        Assert.Equal(200m, pending[0].Amount);
        Assert.Equal(PostingKind.Incurral, pending[0].Kind);
    }

    [Fact]
    public async Task PostIncurral_CalledTwiceWithSameSourceKey_ProducesExactlyOneEntry()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var sourceKey = $"payable-incurral:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        await posting.PostIncurralAsync(companyId, sourceKey, 100m, expenseId, [], CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Second call with same key — must be idempotent.
        await posting.PostIncurralAsync(companyId, sourceKey, 100m, expenseId, [], CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var count = await uow.ReadSet<JournalEntry>()
            .CountAsync(e => e.CompanyId == companyId && e.SourceKey == sourceKey && e.DeletedAt == null);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Reversal_AfterSettlement_MarksEntryReversed_AndCreatesReversalEntry()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);
        var paymentKey = $"payment:{Guid.NewGuid()}";

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Register the settlement entry.
        await posting.PostSettlementAsync(companyId, paymentKey, 75m, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Reverse it.
        await posting.ReverseAsync(companyId, paymentKey, "Estorno de pagamento", CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Original entry should be Reversed.
        var original = await uow.ReadSet<JournalEntry>()
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.SourceKey == paymentKey);
        Assert.NotNull(original);
        Assert.Equal(JournalStatus.Reversed, original.Status);

        // A reversal entry linked to the original must exist.
        var reversalKey = $"{paymentKey}:reversal";
        var reversal = await uow.ReadSet<JournalEntry>()
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.SourceKey == reversalKey);
        Assert.NotNull(reversal);
        Assert.Equal(JournalStatus.Posted, reversal.Status);
        Assert.Equal(original.Id, reversal.ReversalOfEntryId);
    }
}
