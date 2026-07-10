using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Cnab;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Integration tests for <see cref="IRemittanceReturnService"/> resolved directly from DI,
/// running against a real Postgres testcontainer.
///
/// Seeding reuses the pattern from <see cref="RemittanceGenerationTests"/>:
///   - seed company + bank account + two payables (TED + PIX)
///   - generate a remittance via <see cref="IRemittanceGenerationService"/>
///   - author a matching .RET using <see cref="CnabRecordBuilder"/> at the CNAB record level
///   - assert conciliation outcomes
/// </summary>
[Collection("Postgres")]
public class RemittanceReturnServiceTests(PostgresFixture fixture)
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record SeedResult(
        Guid CompanyId,
        Guid BankAccountId,
        Guid PayableAId,   // TED, amount=1000
        Guid PayableBId);  // PIX, amount=500

    /// <summary>
    /// Minimal seed: company + Santander bank account + 2 approved payables (TED + PIX).
    /// No vendor C — keeps the remittance to exactly 2 lines.
    /// </summary>
    private static async Task<SeedResult> SeedAsync(ApiFactory factory, HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa ReturnSvc Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new
            {
                companyId = companyPrefixedId,
                code = "CC-RET",
                name = "Return TI",
                annualBudget = 1_000_000m,
                autoApprovalLimit = 0m,
                unitIds = Array.Empty<string>(),
            });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor A — TED
        var vendorARes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor TED Return Ltda",
            tradeName = "TedReturn",
            document = "11.222.333/0001-81",
            email = "tedret@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = new { bankCode = "341", branch = "0001", account = "123456", accountDigit = "7", pixKey = "" },
        });
        vendorARes.EnsureSuccessStatusCode();
        var vendorAPrefixedId = (await ReadJson(vendorARes)).GetProperty("id").GetString()!;

        // Vendor B — PIX
        var vendorBRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor PIX Return SA",
            tradeName = "PixReturn",
            document = "06.990.590/0001-23",
            email = "pixret@test.com",
            phone = "5133335555",
            address = (object?)null,
            paymentTerms = "à vista",
            bankInfo = new { bankCode = "", branch = "", account = "", accountDigit = "", pixKey = "pixret@return.com" },
        });
        vendorBRes.EnsureSuccessStatusCode();
        var vendorBPrefixedId = (await ReadJson(vendorBRes)).GetProperty("id").GetString()!;

        // Bank account: Santander 033
        await using var setupScope = factory.Services.CreateAsyncScope();
        var uow = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "5555",
            "99900001", "3", BankAccountType.Checking, "Conta ReturnSvc", null,
            cnabAgreementCode: "999999999999");
        bankAccount.SetAsDefaultPayer(PaymentMethod.Ted);
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Payable A (TED, 1000)
        var payableARes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorAPrefixedId,
            costCenterId,
            description = "Payable TED Return",
            installments = new[] { new { dueDate = "2026-05-01", amount = 1_000m } },
            type = "Product", // determined product — required before a payable can schedule
        });
        payableARes.EnsureSuccessStatusCode();
        var payableAPrefixedId = (await ReadJson(payableARes)).GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync($"/api/payables/{payableAPrefixedId}/approve", new { })).EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payableAPrefixedId); // → Scheduled
        var payableAId = Guid.Parse(payableAPrefixedId[(payableAPrefixedId.LastIndexOf('_') + 1)..]);

        // Payable B (PIX, 500)
        var payableBRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorBPrefixedId,
            costCenterId,
            description = "Payable PIX Return",
            installments = new[] { new { dueDate = "2026-05-10", amount = 500m } },
            type = "Product", // determined product — required before a payable can schedule
            paymentMethod = "Pix",
        });
        payableBRes.EnsureSuccessStatusCode();
        var payableBPrefixedId = (await ReadJson(payableBRes)).GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync($"/api/payables/{payableBPrefixedId}/approve", new { })).EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payableBPrefixedId); // → Scheduled
        var payableBId = Guid.Parse(payableBPrefixedId[(payableBPrefixedId.LastIndexOf('_') + 1)..]);

        return new SeedResult(companyId, bankAccount.Id, payableAId, payableBId);
    }

    /// <summary>Generate a remittance via the service and return the remittance entity.</summary>
    private static async Task<Remittance> GenerateRemittanceAsync(ApiFactory factory, Guid companyId, Guid bankAccountId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var genService = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var cutoff = new DateOnly(2026, 12, 31);
        var result = await genService.GenerateAsync(companyId, bankAccountId, cutoff, "test-user", CancellationToken.None);
        Assert.True(result.IsSuccess, $"Generation failed: {result.Error}");
        return result.Value.Remittance;
    }

    /// <summary>
    /// Build a minimal CNAB240 return file with one paid Segmento-A (for <paramref name="refPaid"/>)
    /// and one rejected Segmento-A (for <paramref name="refRejected"/>).
    /// Extra unmatched references can be injected via <paramref name="extraEntries"/>.
    /// </summary>
    private static byte[] BuildReturnFile(
        string refPaid,
        decimal paidAmount,
        string refRejected,
        IEnumerable<(string Reference, string OccurrenceCode, decimal Amount)>? extraEntries = null)
    {
        var paidDate = new DateOnly(2026, 6, 20);

        var sb = new StringBuilder();
        sb.Append(BuildFileHeader()); sb.Append("\r\n");
        sb.Append(BuildBatchHeader()); sb.Append("\r\n");

        // Paid line
        sb.Append(BuildSegmentA(refPaid, "BANKREF001          ", paidDate, paidAmount, "BD        ", 1)); sb.Append("\r\n");
        sb.Append(BuildSegmentB(2)); sb.Append("\r\n");

        // Rejected line
        sb.Append(BuildSegmentA(refRejected, "BANKREF002          ", null, 0m, "AG        ", 3)); sb.Append("\r\n");
        sb.Append(BuildSegmentB(4)); sb.Append("\r\n");

        // Extra entries (e.g. unmatched references)
        int seq = 5;
        foreach (var (extraRef, extraCode, extraAmount) in extraEntries ?? [])
        {
            sb.Append(BuildSegmentA(extraRef, "BANKREFEXTRA        ", null, extraAmount, (extraCode + "        ")[..10], seq)); sb.Append("\r\n");
            sb.Append(BuildSegmentB(seq + 1)); sb.Append("\r\n");
            seq += 2;
        }

        sb.Append(BuildBatchTrailer()); sb.Append("\r\n");
        sb.Append(BuildFileTrailer()); sb.Append("\r\n");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessReturnAsync_PaidLine_RegistersPayment_And_RejectedLine_ReleasesInstallment()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        // Generate remittance to get line references.
        var remittance = await GenerateRemittanceAsync(factory, seed.CompanyId, seed.BankAccountId);
        Assert.Equal(2, remittance.Lines.Count);

        // Find which line maps to which payable.
        var lineA = remittance.Lines.First(l => l.PayableId == seed.PayableAId); // TED, 1000
        var lineB = remittance.Lines.First(l => l.PayableId == seed.PayableBId); // PIX, 500

        // Build .RET: lineA paid, lineB rejected.
        var retBytes = BuildReturnFile(lineA.Reference, lineA.Amount, lineB.Reference);

        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
        var processResult = await svc.ProcessReturnAsync(remittance.Id, retBytes, "test.ret", "operator", Guid.NewGuid(), CancellationToken.None);
        Assert.True(processResult.IsSuccess, $"ProcessReturn failed: {processResult.Error}");

        var outcome = processResult.Value;
        Assert.Equal(2, outcome.Matched);
        Assert.Equal(1, outcome.Paid);
        Assert.Equal(1, outcome.Returned);
        Assert.Equal(0, outcome.Unmatched);
        Assert.Equal(0, outcome.Divergent);

        // Verify DB state in a new scope.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Line A: Paid, installment Paid, Payment exists.
        var verifyLineA = await uow.ReadSet<RemittanceLine>().FirstAsync(l => l.Id == lineA.Id);
        Assert.Equal(RemittanceLineStatus.Paid, verifyLineA.Status);

        var payableA = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .ThenInclude(i => i.Payments)
            .FirstAsync(p => p.Id == seed.PayableAId);
        var installA = payableA.Installments.Single();
        Assert.Equal(InstallmentStatus.Paid, installA.Status);
        Assert.Single(installA.Payments, p => !p.IsReversed);

        // Line B: Returned, installment back to Open.
        var verifyLineB = await uow.ReadSet<RemittanceLine>().FirstAsync(l => l.Id == lineB.Id);
        Assert.Equal(RemittanceLineStatus.Returned, verifyLineB.Status);
        Assert.NotNull(verifyLineB.ReturnReason);

        var payableB = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableBId);
        Assert.All(payableB.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));

        // RemittanceReturn record persisted.
        var returnRecord = await uow.ReadSet<RemittanceReturn>()
            .FirstOrDefaultAsync(r => r.MatchedCount == 2 && r.PaidCount == 1 && r.ReturnedCount == 1);
        Assert.NotNull(returnRecord);

        // Remittance is PartiallyReturned.
        var verifyRemittance = await uow.ReadSet<Remittance>()
            .FirstAsync(r => r.Id == remittance.Id);
        Assert.Equal(RemittanceStatus.PartiallyReturned, verifyRemittance.Status);
    }

    [Fact]
    public async Task ProcessReturnAsync_SameFile_Twice_IsIdempotent()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var remittance = await GenerateRemittanceAsync(factory, seed.CompanyId, seed.BankAccountId);
        var lineA = remittance.Lines.First(l => l.PayableId == seed.PayableAId);
        var lineB = remittance.Lines.First(l => l.PayableId == seed.PayableBId);

        var retBytes = BuildReturnFile(lineA.Reference, lineA.Amount, lineB.Reference);

        // Process once.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
            var r = await svc.ProcessReturnAsync(remittance.Id, retBytes, "test.ret", "op", Guid.NewGuid(), CancellationToken.None);
            Assert.True(r.IsSuccess);
        }

        // Process again with the same file.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
            var r = await svc.ProcessReturnAsync(remittance.Id, retBytes, "test.ret", "op", Guid.NewGuid(), CancellationToken.None);
            Assert.True(r.IsSuccess);
        }

        // Still only 1 Payment on installment A.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var payableA = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .ThenInclude(i => i.Payments)
            .FirstAsync(p => p.Id == seed.PayableAId);
        Assert.Single(payableA.Installments.SelectMany(i => i.Payments), p => !p.IsReversed);
    }

    [Fact]
    public async Task ProcessReturnAsync_UnmatchedReference_CountedButNoError()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var remittance = await GenerateRemittanceAsync(factory, seed.CompanyId, seed.BankAccountId);
        var lineA = remittance.Lines.First(l => l.PayableId == seed.PayableAId);
        var lineB = remittance.Lines.First(l => l.PayableId == seed.PayableBId);

        // Add an extra line with an unmatched reference.
        var retBytes = BuildReturnFile(
            lineA.Reference, lineA.Amount,
            lineB.Reference,
            extraEntries: [("XXXXXXXXXXXXXXXXXX00", "00", 0m)]);

        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
        var result = await svc.ProcessReturnAsync(remittance.Id, retBytes, "test-unmatched.ret", "op", Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success: {result.Error}");
        Assert.Equal(1, result.Value.Unmatched); // the extra fake reference
        Assert.Equal(2, result.Value.Matched);   // lineA + lineB
    }

    [Fact]
    public async Task ProcessReturnAsync_ScopedToRemittance_DoesNotTouchSiblingRemittanceLines()
    {
        // Multitenant safety: two independent remittances (A and B) seeded in the same Postgres container.
        // Both generate lines whose References start from the same sequence (000001xxxxx) because each
        // remittance starts at sequence 1.  Processing remittance A's .RET must NOT touch remittance B's
        // lines, even though their References are lexically identical.
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Seed two independent companies / bank accounts / payables.
        var seedA = await SeedAsync(factory, client);
        var seedB = await SeedAsync(factory, client);

        // Generate a remittance for each company.
        var remittanceA = await GenerateRemittanceAsync(factory, seedA.CompanyId, seedA.BankAccountId);
        var remittanceB = await GenerateRemittanceAsync(factory, seedB.CompanyId, seedB.BankAccountId);

        Assert.True(remittanceA.Lines.Count > 0, "Remittance A must have at least one line.");
        Assert.True(remittanceB.Lines.Count > 0, "Remittance B must have at least one line.");

        // Pick the first line from each remittance.
        var lineA1 = remittanceA.Lines.First(l => l.PayableId == seedA.PayableAId);
        var lineB1 = remittanceB.Lines.First(l => l.PayableId == seedB.PayableAId);

        // Both lines should have the same Reference because the sequence counters restart per remittance.
        // If they differ (rare, due to prior test data) the test still proves isolation — a file with
        // lineA1's reference will match A's line and leave B's line Pending regardless.

        // Build a .RET that marks lineA1 as paid and lineA's second line (if any) as rejected.
        var lineA2 = remittanceA.Lines.FirstOrDefault(l => l.PayableId == seedA.PayableBId);
        var retBytesA = lineA2 is not null
            ? BuildReturnFile(lineA1.Reference, lineA1.Amount, lineA2.Reference)
            : BuildReturnFile(lineA1.Reference, lineA1.Amount, "XXXXXXXXXXXXXXXXXX00");

        // Process remittance A's .RET — scoped to remittanceA.
        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
        var result = await svc.ProcessReturnAsync(remittanceA.Id, retBytesA, "a.ret", "op", Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsSuccess, $"ProcessReturn for A failed: {result.Error}");

        // Verify remittance B's lines are untouched — all still Pending.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        foreach (var lineB in remittanceB.Lines)
        {
            var dbLine = await uow.ReadSet<RemittanceLine>().FirstAsync(l => l.Id == lineB.Id);
            Assert.True(dbLine.Status == RemittanceLineStatus.Pending,
                $"Remittance B line {lineB.Id} (ref '{lineB.Reference}') should still be Pending after processing A's .RET, but was {dbLine.Status}.");
        }

        // Also verify remittance A's lineA1 was actually paid (proves the test exercised the paid path).
        var dbLineA1 = await uow.ReadSet<RemittanceLine>().FirstAsync(l => l.Id == lineA1.Id);
        Assert.Equal(RemittanceLineStatus.Paid, dbLineA1.Status);

        // Clean up B's lines so remaining tests in the shared container see no orphaned Pending refs.
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupSvc = cleanupScope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
        foreach (var lineB in remittanceB.Lines)
            await cleanupSvc.MarkLineReturnedAsync(lineB.Id, "Collision-test cleanup", CancellationToken.None);
    }

    [Fact]
    public async Task ConfirmLinePaidAsync_PendingLine_RegistersPaymentAndMarksPaid()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var remittance = await GenerateRemittanceAsync(factory, seed.CompanyId, seed.BankAccountId);
        // Pick a pending line (TED line).
        var lineA = remittance.Lines.First(l => l.PayableId == seed.PayableAId);

        var operatorId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
        var result = await svc.ConfirmLinePaidAsync(lineA.Id, operatorId, CancellationToken.None);

        Assert.True(result.IsSuccess, $"ConfirmLinePaidAsync failed: {result.Error}");

        // Verify.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var verifyLine = await uow.ReadSet<RemittanceLine>().FirstAsync(l => l.Id == lineA.Id);
        Assert.Equal(RemittanceLineStatus.Paid, verifyLine.Status);

        var payableA = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .ThenInclude(i => i.Payments)
            .FirstAsync(p => p.Id == seed.PayableAId);
        var installA = payableA.Installments.Single();
        Assert.Equal(InstallmentStatus.Paid, installA.Status);
        Assert.Single(installA.Payments, p => !p.IsReversed);
    }

    [Fact]
    public async Task ConfirmLinePaidAsync_NonPendingLine_ReturnsFailure()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var remittance = await GenerateRemittanceAsync(factory, seed.CompanyId, seed.BankAccountId);
        var lineA = remittance.Lines.First(l => l.PayableId == seed.PayableAId);

        // Mark as paid first via ConfirmLinePaidAsync.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
            var r = await svc.ConfirmLinePaidAsync(lineA.Id, Guid.NewGuid(), CancellationToken.None);
            Assert.True(r.IsSuccess);
        }

        // Try to confirm again — should fail.
        await using var scope2 = factory.Services.CreateAsyncScope();
        var svc2 = scope2.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
        var result = await svc2.ConfirmLinePaidAsync(lineA.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("RemittanceLine.NotPending", result.Error.Code);
    }

    [Fact]
    public async Task MarkLineReturnedAsync_PendingLine_ReleasesInstallmentAndMarksReturned()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var remittance = await GenerateRemittanceAsync(factory, seed.CompanyId, seed.BankAccountId);
        // Pick the TED line — installment is InRemittance after generation.
        var lineA = remittance.Lines.First(l => l.PayableId == seed.PayableAId);
        var lineB = remittance.Lines.First(l => l.PayableId == seed.PayableBId);

        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
        var result = await svc.MarkLineReturnedAsync(lineA.Id, "Manual rejection test", CancellationToken.None);

        Assert.True(result.IsSuccess, $"MarkLineReturnedAsync failed: {result.Error}");

        // Also mark lineB returned so no Pending lines remain in the shared DB.
        // Tests in the same collection share one Postgres container; leaving Pending lines with
        // sequence-scoped references (e.g. "00000100002") would collide with subsequent tests.
        var resultB = await svc.MarkLineReturnedAsync(lineB.Id, "Cleanup rejection", CancellationToken.None);
        Assert.True(resultB.IsSuccess, $"MarkLineReturnedAsync lineB cleanup failed: {resultB.Error}");

        // Verify DB state in a new scope.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // LineA must be Returned with the specified reason.
        var verifyLine = await uow.ReadSet<RemittanceLine>().FirstAsync(l => l.Id == lineA.Id);
        Assert.Equal(RemittanceLineStatus.Returned, verifyLine.Status);
        Assert.Equal("Manual rejection test", verifyLine.ReturnReason);

        // InstallmentA must be back to Open.
        var payableA = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableAId);
        Assert.All(payableA.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));

        // Remittance fully recomputed — both lines Returned, no Pending → PartiallyReturned.
        var verifyRemittance = await uow.ReadSet<Remittance>()
            .FirstAsync(r => r.Id == remittance.Id);
        Assert.Equal(RemittanceStatus.PartiallyReturned, verifyRemittance.Status);
    }

    [Fact]
    public async Task MarkLineReturnedAsync_NonPendingLine_ReturnsFailure()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var remittance = await GenerateRemittanceAsync(factory, seed.CompanyId, seed.BankAccountId);
        var lineA = remittance.Lines.First(l => l.PayableId == seed.PayableAId);
        var lineB = remittance.Lines.First(l => l.PayableId == seed.PayableBId);

        // Mark both lines returned to leave a clean DB; lineA is returned first so it becomes non-Pending.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
            var r = await svc.MarkLineReturnedAsync(lineA.Id, "First rejection", CancellationToken.None);
            Assert.True(r.IsSuccess);
            // Cleanup lineB so no Pending lines remain in the shared DB.
            var rb = await svc.MarkLineReturnedAsync(lineB.Id, "Cleanup rejection", CancellationToken.None);
            Assert.True(rb.IsSuccess);
        }

        // Attempt to mark lineA returned again — line is no longer Pending.
        await using var scope2 = factory.Services.CreateAsyncScope();
        var svc2 = scope2.ServiceProvider.GetRequiredService<IRemittanceReturnService>();
        var result = await svc2.MarkLineReturnedAsync(lineA.Id, "Second rejection", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("RemittanceLine.NotPending", result.Error.Code);

        // Verify no state change occurred — lineA still Returned with original reason.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var verifyLine = await uow.ReadSet<RemittanceLine>().FirstAsync(l => l.Id == lineA.Id);
        Assert.Equal(RemittanceLineStatus.Returned, verifyLine.Status);
        Assert.Equal("First rejection", verifyLine.ReturnReason);
    }

    // -----------------------------------------------------------------------
    // CNAB record builders (mirrors Cnab240ReturnParserTests golden file)
    // -----------------------------------------------------------------------

    private static byte[] BuildReturnFileFromSegments(IEnumerable<string> segmentALines)
    {
        var sb = new StringBuilder();
        sb.Append(BuildFileHeader()); sb.Append("\r\n");
        sb.Append(BuildBatchHeader()); sb.Append("\r\n");
        int seq = 1;
        foreach (var segA in segmentALines)
        {
            sb.Append(segA); sb.Append("\r\n");
            sb.Append(BuildSegmentB(seq + 1)); sb.Append("\r\n");
            seq += 2;
        }
        sb.Append(BuildBatchTrailer()); sb.Append("\r\n");
        sb.Append(BuildFileTrailer()); sb.Append("\r\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string BuildFileHeader()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "0");
        b.Numeric(8, 1, "0");
        b.Blank(9, 9);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Alpha(33, 20, "0033555599999999");
        b.Numeric(53, 5, "0");
        b.Blank(58, 1);
        b.Numeric(59, 12, "0");
        b.Alpha(71, 1, "0");
        b.Blank(72, 1);
        b.Alpha(73, 30, "EMPRESA RETURNSVC TEST");
        b.Alpha(103, 30, "BANCO SANTANDER");
        b.Blank(133, 10);
        b.Numeric(143, 1, "2");
        b.Numeric(144, 8, "20062026");
        b.Numeric(152, 6, "100000");
        b.Numeric(158, 6, "1");
        b.Numeric(164, 3, "060");
        b.Numeric(167, 5, "0");
        b.Blank(172, 20);
        b.Blank(192, 20);
        b.Blank(212, 19);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    private static string BuildBatchHeader()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "1");
        b.Alpha(9, 1, "C");
        b.Numeric(10, 2, "20");
        b.Numeric(12, 2, "03");
        b.Numeric(14, 3, "031");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Alpha(33, 20, "0033555599999999");
        b.Numeric(53, 5, "0");
        b.Blank(58, 1);
        b.Numeric(59, 12, "0");
        b.Alpha(71, 1, "0");
        b.Blank(72, 1);
        b.Alpha(73, 30, "EMPRESA RETURNSVC TEST");
        b.Blank(103, 40);
        b.Blank(143, 30);
        b.Numeric(173, 5, "0");
        b.Blank(178, 15);
        b.Blank(193, 20);
        b.Numeric(213, 5, "0");
        b.Numeric(218, 3, "0");
        b.Blank(221, 2);
        b.Blank(223, 8);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    private static string BuildSegmentA(
        string reference,
        string bankRef,
        DateOnly? actualDate,
        decimal actualValue,
        string occurrences10,
        int seq)
    {
        // Pad reference to exactly 20 chars (left-justified, right-padded with spaces).
        var refPadded = reference.Length < 20 ? reference.PadRight(20) : reference[..20];

        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, seq);
        b.Alpha(14, 1, "A");
        b.Numeric(15, 1, "0");
        b.Numeric(16, 2, "0");
        b.Numeric(18, 3, "18");
        b.Numeric(21, 3, "341");
        b.Numeric(24, 5, "0001");
        b.Blank(29, 1);
        b.Numeric(30, 12, "123456");
        b.Alpha(42, 1, "7");
        b.Blank(43, 1);
        b.Alpha(44, 30, "FORNECEDOR TEST", allowTruncate: true);
        b.Alpha(74, 20, refPadded, allowTruncate: true);
        b.Date(94, 8, new DateOnly(2026, 5, 1));
        b.Alpha(102, 3, "BRL");
        b.Numeric(105, 15, "0");
        b.Money(120, 15, 1_000m);
        b.Alpha(135, 20, bankRef[..20], allowTruncate: true);
        if (actualDate.HasValue)
            b.Date(155, 8, actualDate.Value);
        else
            b.Numeric(155, 8, "0");
        b.Money(163, 15, actualValue);
        b.Blank(178, 40);
        b.Blank(218, 2);
        b.Alpha(220, 5, "00001");
        b.Blank(225, 2);
        b.Blank(227, 3);
        b.Numeric(230, 1, "0");
        b.Alpha(231, 10, occurrences10[..10], allowTruncate: true);
        return b.BuildComplete();
    }

    private static string BuildSegmentB(int seq)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, seq);
        b.Alpha(14, 1, "B");
        b.Numeric(15, 2, "0");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Blank(33, 35);
        b.Blank(68, 60);
        b.Blank(128, 99);
        b.Blank(227, 6);
        b.Blank(233, 8);
        return b.BuildComplete();
    }

    private static string BuildBatchTrailer()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "5");
        b.Blank(9, 9);
        b.Numeric(18, 6, "4");
        b.Money(24, 18, 1_000m);
        b.Numeric(42, 18, "0");
        b.Numeric(60, 6, "0");
        b.Blank(66, 165);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    private static string BuildFileTrailer()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "9999");
        b.Numeric(8, 1, "9");
        b.Blank(9, 9);
        b.Numeric(18, 6, "1");
        b.Numeric(24, 6, "6");
        b.Blank(30, 6);
        b.Blank(36, 205);
        return b.BuildComplete();
    }
}
