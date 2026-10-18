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

/// <summary>Edge case A (#7 × #1): a CaP incurral whose competence lands in a CLOSED period must be clamped
/// to the current open period — never parked — so it stays paired with its settlement (no orphaned debit on
/// Fornecedores a Pagar). Driven against the real seeded chart + real AccountingPeriod rows.</summary>
[Collection("Postgres")]
public class PayablePostingClosedPeriodTests(PostgresFixture fixture)
{
    private const string ExpenseCode = "4.2.15.05.020";
    private const string LiabilityCode = "2.2.05.15.080";

    private static async Task<Guid> SeedCompanyAndChartAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "Empresa Período Fechado", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var prefixedId = JsonDocument.Parse(await companyRes.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetString()!;
        var companyId = CompanyId.Parse(prefixedId, null).Value;
        (await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { })).EnsureSuccessStatusCode();
        return companyId;
    }

    private static async Task<Guid> AccountIdAsync(IUnitOfWork uow, string code) =>
        (await uow.ReadSet<LedgerAccount>().FirstAsync(a => a.Code == code)).Id;

    [Fact]
    public async Task Incurral_in_closed_period_clamps_to_open_and_keeps_settlement_paired()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAndChartAsync(client);

        // Close March 2026 (the incurral's competence month).
        (await client.PostAsJsonAsync("/api/accounting/periods/close",
            new { companyId = $"company_{companyId:N}", fiscalYear = 2026, month = 3 })).EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IPayablePostingService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var expenseId = await AccountIdAsync(uow, ExpenseCode);
        var payableId = Guid.NewGuid();

        // Incurral competence in the CLOSED March period; settlement paid today (open). These arrive as two
        // separate consume operations in production, so each commits on its own (distinct entry Number).
        await posting.PostIncurralAsync(companyId, $"payable-incurral:{payableId}", 100m, expenseId, [],
            competenceDate: new DateOnly(2026, 3, 10), costCenterId: null, unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);
        await posting.PostSettlementAsync(companyId, $"payment:{payableId}", 100m, today, cashAccountId: null, withholdings: [], unitId: null, documentRef: null, counterpartyName: null, counterpartyDocument: null, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // The incurral was clamped (posted), not parked.
        Assert.False(await uow.ReadSet<PendingAccountingPosting>().AnyAsync(p => p.CompanyId == companyId));
        var incurral = await uow.ReadSet<JournalEntry>()
            .FirstAsync(e => e.CompanyId == companyId && e.SourceKey == $"payable-incurral:{payableId}");
        Assert.Equal(JournalStatus.Posted, incurral.Status);
        Assert.Equal(today, incurral.CompetenceDate); // clamped out of closed March

        // Fornecedores a Pagar is paired: incurral credit (100) == settlement debit (100) → no orphan.
        var liabilityId = await AccountIdAsync(uow, LiabilityCode);
        var lines = await uow.ReadSet<JournalEntry>()
            .Where(e => e.CompanyId == companyId)
            .SelectMany(e => e.Lines)
            .Where(l => l.LedgerAccountId == liabilityId)
            .ToListAsync();
        var credit = lines.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount);
        var debit = lines.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount);
        Assert.Equal(100m, credit);
        Assert.Equal(100m, debit);
    }
}
