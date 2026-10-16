using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Messaging;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Persistence;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>End-to-end of the análise tributária stage over the requisição→payable flow (AEL-948): a
/// Serviço/Conta Fixa requisition, once approved, parks the Payable in <c>PendingTaxAnalysis</c> — blocking both
/// payment and the accounting incurral (no <c>PayableScheduled</c>) — until the Tributário validates the retenções
/// and releases it (NF-gated). Consumers are driven directly (no broker), the outbox is asserted via SQL.</summary>
[Collection("Postgres")]
public class PayableTaxAnalysisEndpointTests(PostgresFixture fixture)
{
    private static Guid Raw(string typedId) => Guid.ParseExact(typedId[(typedId.LastIndexOf('_') + 1)..], "N");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static RequisitionApprovedForPaymentConsumer NewApprovalConsumer(IServiceProvider sp) =>
        new(sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IPublishEndpoint>(),
            sp.GetRequiredService<IRequisitionAttachmentCopier>(),
            sp.GetRequiredService<ILogger<RequisitionApprovedForPaymentConsumer>>());

    /// <summary>How many <c>PayableScheduled</c> outbox messages exist for this specific payable (scoped by the
    /// payable id in the body, since the outbox is shared across the suite's tests).</summary>
    private static async Task<int> ScheduledOutboxCountAsync(AppDbContext db, Guid payableId)
    {
        var pattern = $"%{payableId}%";
        return await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*)::int AS \"Value\" FROM masstransit.outbox_message WHERE message_type LIKE '%PayableScheduled%' AND body::text LIKE {pattern}")
            .SingleAsync();
    }

    private static async Task<PayableStatus> StatusAsync(ApiFactory factory, Guid payableId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<Payable>().Where(p => p.Id == payableId).Select(p => p.Status).SingleAsync();
    }

    /// <summary>Seeds a company/unit/cost-center/vendor, then drives a Serviço requisition (RequiresTaxAnalysis)
    /// through both Finance consumers so the Payable ends parked in <c>PendingTaxAnalysis</c>. Returns the ids.</summary>
    private static async Task<(string CompanyId, Guid PayableRawId, string PayablePrefixedId)> ParkTaxTitleAsync(
        ApiFactory factory, HttpClient client, decimal amount = 1000m, bool contaFixa = false)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "TI");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Serviço");

        var requisitionId = Guid.NewGuid();
        var msg = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Serviço de consultoria", "2026-07", "Service", new DateOnly(2026, 7, 15),
            [new PlannedInstallment(1, new DateOnly(2026, 7, 10), amount, "Pix")], [],
            RequiresFiscalDocument: contaFixa, RequiresTaxAnalysis: true);

        // Hop 1: the requisition consumer creates the Payable (Received) and stamps RequiresTaxAnalysis.
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(msg, default);

        Guid payableRawId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            payableRawId = await db.Set<Payable>().Where(p => p.OriginRequisitionId == requisitionId)
                .Select(p => p.Id).SingleAsync();
        }

        // Hop 2: the pipeline consumer classifies + auto-advances — for a tax title it PARKS in PendingTaxAnalysis.
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>().ProcessAsync(payableRawId, default);

        return (companyId, payableRawId, new PayableId(payableRawId).ToString());
    }

    [Fact]
    public async Task Service_requisition_parks_in_tax_analysis_then_tax_releases_to_scheduled()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"tax_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, rawId, payableId) = await ParkTaxTitleAsync(factory, admin);

        // --- Parked: blocking both accounting posting and payment. ---
        Assert.Equal(PayableStatus.PendingTaxAnalysis, await StatusAsync(factory, rawId));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await ScheduledOutboxCountAsync(db, rawId)); // no incurral event → Contabilidade can't post
        }
        var parked = await ReadJson(await admin.GetAsync($"/api/payables/stage-summary?companyId={companyId}"));
        Assert.Equal(1, parked.GetProperty("taxAnalysis").GetProperty("count").GetInt32());
        Assert.Equal(0, parked.GetProperty("payment").GetProperty("count").GetInt32());
        Assert.Equal(0, parked.GetProperty("accounting").GetProperty("count").GetInt32());
        // Without an NF the parked tax-analysis title still counts as "NF pending" on the dashboard
        // (review #2: the bucket previously ignored PendingTaxAnalysis and silently under-counted).
        Assert.Equal(1, parked.GetProperty("fiscalDocumentPending").GetProperty("count").GetInt32());

        // --- Tributário (payables:tax) validates the retenções and cannot release without an NF. ---
        var tax = factory.CreateClientWith(Roles.Tax, Permissions.PayablesTax, Permissions.PayablesRead);
        await FinanceSeed.SetWithholdingAsync(tax, payableId, baseAmount: 1000m, amount: 15m); // IRRF adjusted by the Tributário

        var blocked = await tax.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal("payable.fiscal_document_missing", (await ReadJson(blocked)).GetProperty("error_code").GetString());
        Assert.Equal(PayableStatus.PendingTaxAnalysis, await StatusAsync(factory, rawId)); // still parked

        // --- With the NF registered, the Tributário releases it → Scheduled + the accounting incurral fires. ---
        await FinanceSeed.RegisterFiscalDocAsync(factory, admin, payableId);
        var released = await tax.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal("Scheduled", (await ReadJson(released)).GetProperty("status").GetString());

        Assert.Equal(PayableStatus.Scheduled, await StatusAsync(factory, rawId));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await ScheduledOutboxCountAsync(db, rawId)); // released → PayableScheduled published once
        }
        var afterRelease = await ReadJson(await admin.GetAsync($"/api/payables/stage-summary?companyId={companyId}"));
        Assert.Equal(0, afterRelease.GetProperty("taxAnalysis").GetProperty("count").GetInt32());
        Assert.Equal(1, afterRelease.GetProperty("payment").GetProperty("count").GetInt32());

        // --- And it is forwarded to Contabilidade: the released title (scheduled without a ledger account) must
        // appear in the accountant's classification queue (reviewPending) so Contábil picks its conta contábil. ---
        var review = await ReadJson(await admin.GetAsync($"/api/payables/stage-items?companyId={companyId}&reviewPending=true"));
        var reviewIds = review.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("payableId").GetString()).ToList();
        Assert.Contains(payableId, reviewIds);
    }

    [Fact]
    public async Task Tax_release_gate_is_type_aware__boleto_does_not_clear__nota_fiscal_does()
    {
        // AEL-959: the "NF presente" gate is type-aware — a plain attachment (boleto/quote) no longer clears
        // it; only a parsed FiscalDocument or an attachment typed as Nota Fiscal does.
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"tax_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, rawId, payableId) = await ParkTaxTitleAsync(factory, admin);
        var tax = factory.CreateClientWith(Roles.Tax, Permissions.PayablesTax, Permissions.PayablesRead);

        // A non-NF attachment (Boleto) must NOT clear the gate — release still blocked, still NF-pending.
        // (The seed sends XML bytes, so the file name must be .xml to pass content validation.)
        await FinanceSeed.AttachDocumentAsync(admin, payableId, "boleto.xml", kind: "BankSlip");
        var blocked = await tax.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal("payable.fiscal_document_missing", (await ReadJson(blocked)).GetProperty("error_code").GetString());
        Assert.Equal(PayableStatus.PendingTaxAnalysis, await StatusAsync(factory, rawId));
        var stillPending = await ReadJson(await admin.GetAsync($"/api/payables/stage-summary?companyId={companyId}"));
        Assert.Equal(1, stillPending.GetProperty("fiscalDocumentPending").GetProperty("count").GetInt32());

        // An attachment typed as Nota Fiscal clears the gate → release to Scheduled.
        await FinanceSeed.AttachDocumentAsync(admin, payableId, "nota.xml", kind: "FiscalDocument");
        var released = await tax.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal(PayableStatus.Scheduled, await StatusAsync(factory, rawId));
    }

    [Fact]
    public async Task Conta_Fixa_releases_with_a_Fatura__Fatura_OU_NF()
    {
        // AEL-959: for a Conta Fixa title (RequiresFiscalDocument) a Fatura clears the gate just like a Nota Fiscal;
        // a plain Boleto still does not.
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"cf_{Guid.NewGuid():N}@ulbra.br");
        var (_, rawId, payableId) = await ParkTaxTitleAsync(factory, admin, contaFixa: true);
        var tax = factory.CreateClientWith(Roles.Tax, Permissions.PayablesTax, Permissions.PayablesRead);

        await FinanceSeed.AttachDocumentAsync(admin, payableId, "boleto.xml", kind: "BankSlip");
        var blocked = await tax.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(PayableStatus.PendingTaxAnalysis, await StatusAsync(factory, rawId));

        await FinanceSeed.AttachDocumentAsync(admin, payableId, "fatura.xml", kind: "Invoice");
        var released = await tax.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal(PayableStatus.Scheduled, await StatusAsync(factory, rawId));
    }

    [Fact]
    public async Task Non_conta_fixa_is_not_cleared_by_a_Fatura__NF_only()
    {
        // For a non-Conta-Fixa (Serviço) title a Fatura does NOT qualify — only a parsed FiscalDocument or an NF attachment.
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"svc_{Guid.NewGuid():N}@ulbra.br");
        var (_, rawId, payableId) = await ParkTaxTitleAsync(factory, admin); // Serviço: RequiresFiscalDocument=false
        var tax = factory.CreateClientWith(Roles.Tax, Permissions.PayablesTax, Permissions.PayablesRead);

        await FinanceSeed.AttachDocumentAsync(admin, payableId, "fatura.xml", kind: "Invoice");
        var blocked = await tax.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal("payable.fiscal_document_missing", (await ReadJson(blocked)).GetProperty("error_code").GetString());
        Assert.Equal(PayableStatus.PendingTaxAnalysis, await StatusAsync(factory, rawId));
    }

    [Fact]
    public async Task Tax_release_without_payables_tax_permission_is_forbidden()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"tax_{Guid.NewGuid():N}@ulbra.br");
        var (_, rawId, payableId) = await ParkTaxTitleAsync(factory, admin);
        await FinanceSeed.RegisterFiscalDocAsync(factory, admin, payableId); // NF present, so only the permission blocks

        // Finance holds payables:write/pay but NOT payables:tax — releasing is the Tributário's alone.
        var finance = factory.CreateClientWith(Roles.Finance, Permissions.PayablesWrite, Permissions.PayablesPay);
        var res = await finance.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(PayableStatus.PendingTaxAnalysis, await StatusAsync(factory, rawId));
    }

    [Fact]
    public async Task Tax_release_on_a_non_tax_title_returns_409()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"tax_{Guid.NewGuid():N}@ulbra.br");

        // A Manual (non-tax) payable never enters PendingTaxAnalysis — it parks in PendingAccounting after approval.
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var cc = await FinanceSeed.CreateCostCenterAsync(admin, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "TI");
        var vendor = await FinanceSeed.CreateVendorAsync(admin, "Fornecedor Produto");
        var payableId = await FinanceSeed.CreatePayableAsync(
            factory, admin, companyId, vendor, cc, FinanceSeed.Today().AddDays(30), 500m, approve: false);
        await FinanceSeed.ApproveAsync(admin, payableId); // → PendingAccounting
        // NF present, so the fiscal-document gate passes and the state guard is what rejects the release.
        await FinanceSeed.RegisterFiscalDocAsync(factory, admin, payableId);

        var tax = factory.CreateClientWith(Roles.Tax, Permissions.PayablesTax, Permissions.PayablesRead);
        var res = await tax.PostAsJsonAsync($"/api/payables/{payableId}/tax-analysis/confirm", new { });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("payable.invalid_state", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Request_fiscal_document_allows_payables_tax_and_forbids_neither_permission()
    {
        // "Solicitar NF" is gated any-of(payables:write, payables:tax) — it backs the Tributário's button. The
        // title is requisition-sourced (has a requester to notify), so the request succeeds.
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"tax_{Guid.NewGuid():N}@ulbra.br");
        var (_, _, payableId) = await ParkTaxTitleAsync(factory, admin);

        // Tributário (payables:tax, no payables:write) can still request the NF.
        var tax = factory.CreateClientWith(Roles.Tax, Permissions.PayablesTax, Permissions.PayablesRead);
        var ok = await tax.PostAsJsonAsync($"/api/payables/{payableId}/request-fiscal-document", new { });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        // Neither payables:write nor payables:tax → 403.
        var noPerm = factory.CreateClientWith(Roles.Requester, Permissions.RequisitionsRead);
        var forbidden = await noPerm.PostAsJsonAsync($"/api/payables/{payableId}/request-fiscal-document", new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
