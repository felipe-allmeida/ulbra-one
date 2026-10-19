using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableStageSummaryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StageSummary_groups_count_and_total_by_stage()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, "Fornecedor A");

        // Two payables left in the default 'Received' stage (Entry); one approved -> Scheduled (Payment).
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 100m, approve: false);
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 200m, approve: false);
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 50m, approve: true);

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

    [Fact]
    public async Task StageSummary_payment_bucket_reports_net_total()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, "Fornecedor A");

        // Scheduled with withholding: 1000 gross − 150 IRRF = 850 net. The withholding is set AFTER the NF
        // is registered: the seed's NF is an NF-e (Product), and the auto-withholding pass clears retentions
        // on a Product (AEL-696). Setting it post-NF, while still PendingAccounting, makes it stick (confirm
        // does not recalculate), so the payable reaches Scheduled at net 850.
        var withheld = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 1000m, approve: false);
        await FinanceSeed.ApproveAsync(client, withheld);
        await FinanceSeed.RegisterFiscalDocAsync(factory, client, withheld);
        await FinanceSeed.SetWithholdingAsync(client, withheld, baseAmount: 1000m, amount: 150m);
        await FinanceSeed.ConfirmAccountingAsync(client, withheld);

        // Scheduled without withholding: net == gross.
        var plain = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 500m, approve: true);
        _ = plain;

        // PendingAccounting with withholding must NOT leak into the Payment net sum.
        var pending = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 2000m, approve: false);
        await FinanceSeed.SetWithholdingAsync(client, pending, baseAmount: 2000m, amount: 300m);
        await FinanceSeed.ApproveAsync(client, pending);

        var body = JsonDocument.Parse(await (await client.GetAsync($"/api/payables/stage-summary?companyId={companyId}"))
            .Content.ReadAsStringAsync()).RootElement;

        var payment = body.GetProperty("payment");
        Assert.Equal(2, payment.GetProperty("count").GetInt32());
        Assert.Equal(1500m, payment.GetProperty("totalAmount").GetDecimal()); // gross sum unchanged
        Assert.Equal(1350m, payment.GetProperty("netTotalAmount").GetDecimal()); // 850 + 500

        // Only the Payment bucket carries a net sum; the others serialize null.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("accounting").GetProperty("netTotalAmount").ValueKind);

        // A company with nothing scheduled reports 0 (not null) on the Payment bucket.
        var emptyCompany = await FinanceSeed.CreateCompanyAsync(client);
        var empty = JsonDocument.Parse(await (await client.GetAsync($"/api/payables/stage-summary?companyId={emptyCompany}"))
            .Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0m, empty.GetProperty("payment").GetProperty("netTotalAmount").GetDecimal());
    }

    [Fact]
    public async Task StageSummary_reports_fiscalDocumentPending_bucket()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, "Fornecedor A");

        // PendingAccounting WITHOUT NF -> counts toward fiscalDocumentPending.
        var pendingA = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 100m, approve: false);
        await FinanceSeed.ApproveAsync(client, pendingA);
        var pendingB = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 200m, approve: false);
        await FinanceSeed.ApproveAsync(client, pendingB);

        // PendingAccounting WITH NF -> in the Accounting bucket but NOT in fiscalDocumentPending.
        var pendingWithNf = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 400m, approve: false);
        await FinanceSeed.ApproveAsync(client, pendingWithNf);
        await FinanceSeed.RegisterFiscalDocAsync(factory, client, pendingWithNf);

        // PendingAccounting WITH an attached document (no parsed FiscalDocument) -> also counts as "has NF":
        // NOT in fiscalDocumentPending, but IS in the Accounting bucket.
        var pendingWithAttachment = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 300m, approve: false);
        await FinanceSeed.ApproveAsync(client, pendingWithAttachment);
        await FinanceSeed.AttachDocumentAsync(client, pendingWithAttachment);

        // Received (not approved) and Scheduled (fully accounted) -> neither counts toward fiscalDocumentPending.
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 50m, approve: false);
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 500m, approve: true);

        var body = JsonDocument.Parse(await (await client.GetAsync($"/api/payables/stage-summary?companyId={companyId}")).Content.ReadAsStringAsync()).RootElement;

        // fiscalDocumentPending = the two PendingAccounting titles with NO NF (100 + 200); the fiscal-doc one
        // and the attachment one are both excluded.
        var fiscalPending = body.GetProperty("fiscalDocumentPending");
        Assert.Equal(2, fiscalPending.GetProperty("count").GetInt32());
        Assert.Equal(300m, fiscalPending.GetProperty("totalAmount").GetDecimal());

        // Accounting bucket still counts ALL PendingAccounting (with + without NF): 100 + 200 + 400 + 300.
        var accounting = body.GetProperty("accounting");
        Assert.Equal(4, accounting.GetProperty("count").GetInt32());
        Assert.Equal(1000m, accounting.GetProperty("totalAmount").GetDecimal());

        // Controls: the Received one in Entry, the fully-accounted one in Payment.
        Assert.Equal(1, body.GetProperty("entry").GetProperty("count").GetInt32());
        Assert.Equal(1, body.GetProperty("payment").GetProperty("count").GetInt32());
        Assert.Equal(500m, body.GetProperty("payment").GetProperty("totalAmount").GetDecimal());

        // A company with no such payables reports the zero bucket, not null.
        var emptyCompany = await FinanceSeed.CreateCompanyAsync(client);
        var emptyBody = JsonDocument.Parse(await (await client.GetAsync($"/api/payables/stage-summary?companyId={emptyCompany}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, emptyBody.GetProperty("fiscalDocumentPending").GetProperty("count").GetInt32());
        Assert.Equal(0m, emptyBody.GetProperty("fiscalDocumentPending").GetProperty("totalAmount").GetDecimal());
    }

    /// <summary>
    /// AEL: the Pendências "Nota Fiscal" tab lists ONLY PendingAccounting titles without an NF, but its badge
    /// was reading the global <c>fiscalDocumentPending</c> bucket (which also spans PendingTaxAnalysis /
    /// Scheduled / Paid) — badge said 3, list came back empty. The scoped bucket is what the tab must count.
    /// </summary>
    [Fact]
    public async Task StageSummary_scopes_the_accounting_nf_bucket_to_pending_accounting()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, "Fornecedor A");

        // A: PendingAccounting, no NF -> the only title the "Nota Fiscal" tab can actually list.
        var inAccounting = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 100m, approve: false);
        await FinanceSeed.ApproveAsync(client, inAccounting);

        // B: PendingAccounting WITH NF -> in neither NF bucket.
        var withNf = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 400m, approve: false);
        await FinanceSeed.ApproveAsync(client, withNf);
        await FinanceSeed.RegisterFiscalDocAsync(factory, client, withNf);

        // C/D: no NF, but parked past the accounting queue (Tributação / Pagamento). Both count globally —
        // the Tributário chases C's NF, D's is chased on the title itself — and neither is attachable here.
        // The status is forced directly: the HTTP flow gates both transitions on a registered NF.
        var inTaxAnalysis = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 200m, approve: false);
        await FinanceSeed.ApproveAsync(client, inTaxAnalysis);
        await ForceStatusAsync(factory, inTaxAnalysis, PayableStatus.PendingTaxAnalysis);

        var scheduled = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 50m, approve: false);
        await FinanceSeed.ApproveAsync(client, scheduled);
        await ForceStatusAsync(factory, scheduled, PayableStatus.Scheduled);

        var body = JsonDocument.Parse(await (await client.GetAsync($"/api/payables/stage-summary?companyId={companyId}")).Content.ReadAsStringAsync()).RootElement;

        // Global bucket (dashboard / Tributação aside) keeps every NF pendency: A + C + D.
        var global = body.GetProperty("fiscalDocumentPending");
        Assert.Equal(3, global.GetProperty("count").GetInt32());
        Assert.Equal(350m, global.GetProperty("totalAmount").GetDecimal());

        // Scoped bucket (the tab badge) counts only what the tab lists: A.
        var scoped = body.GetProperty("fiscalDocumentPendingAccounting");
        Assert.Equal(1, scoped.GetProperty("count").GetInt32());
        Assert.Equal(100m, scoped.GetProperty("totalAmount").GetDecimal());

        // A company with nothing pending reports the zero bucket, not null.
        var emptyCompany = await FinanceSeed.CreateCompanyAsync(client);
        var emptyBody = JsonDocument.Parse(await (await client.GetAsync($"/api/payables/stage-summary?companyId={emptyCompany}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, emptyBody.GetProperty("fiscalDocumentPendingAccounting").GetProperty("count").GetInt32());
        Assert.Equal(0m, emptyBody.GetProperty("fiscalDocumentPendingAccounting").GetProperty("totalAmount").GetDecimal());
    }

    /// <summary>Parks a payable in a status the HTTP flow gates behind a registered NF (Scheduled needs one;
    /// PendingTaxAnalysis is requisition-only). A column UPDATE, so the private setter is not in the way.</summary>
    private static async Task ForceStatusAsync(ApiFactory factory, string payableId, PayableStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var id = PayableId.Parse(payableId, null).Value;
        await uow.Set<Payable>().Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status));
    }
}
