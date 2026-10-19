using System.IO;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Budgeting.Infrastructure.Messaging;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Messaging;
using Ulbra.One.IntegrationTests.Finance;
using Ulbra.One.Persistence;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>AEL-793 — the two consumers driven directly against a real Postgres (no broker): the Finance
/// side creates exactly one Payable per approved requisition (idempotent), and the Budgeting side folds a
/// registered payment back into the requisition's realizado. The full outbox round-trip (broker delivery)
/// is exercised live with the docker stack; here we test the consumer cores in a DI scope.</summary>
[Collection("Postgres")]
public class RequisitionFinanceIntegrationTests(PostgresFixture fixture)
{
    private static Guid Raw(string typedId) => Guid.ParseExact(typedId[(typedId.LastIndexOf('_') + 1)..], "N");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static RequisitionApprovedForPaymentConsumer NewApprovalConsumer(IServiceProvider sp) =>
        new(sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IPublishEndpoint>(),
            sp.GetRequiredService<IRequisitionAttachmentCopier>(),
            sp.GetRequiredService<ILogger<RequisitionApprovedForPaymentConsumer>>());

    private static RequisitionCycleAddedForPaymentConsumer NewCycleConsumer(IServiceProvider sp) =>
        new(sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IPublishEndpoint>(),
            sp.GetRequiredService<IRequisitionAttachmentCopier>(),
            sp.GetRequiredService<ILogger<RequisitionCycleAddedForPaymentConsumer>>());

    /// <summary>POSTs a cycle to the multipart <c>/cycles</c> endpoint. The fatura do mês is mandatory (AEL-959),
    /// so a default PDF rides along unless the caller supplies its own file. Mirrors the web client (AEL-877).</summary>
    private static Task<HttpResponseMessage> LaunchCycleAsync(
        HttpClient client, string reqTypedId, string competency, string dueDate, decimal amount,
        string paymentMethod = "Pix", string? boletoDigitableLine = null, (byte[] bytes, string fileName, string contentType)? file = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(competency), "competency" },
            { new StringContent(dueDate), "dueDate" },
            { new StringContent(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "amount" },
            { new StringContent(paymentMethod), "paymentMethod" },
        };
        if (boletoDigitableLine is not null) form.Add(new StringContent(boletoDigitableLine), "boletoDigitableLine");
        var f = file ?? (Encoding.ASCII.GetBytes("%PDF-1.4 fatura do mes"), "fatura.pdf", "application/pdf");
        var content = new ByteArrayContent(f.bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(f.contentType);
        form.Add(content, "file", f.fileName);
        return client.PostAsync($"/api/requisitions/{reqTypedId}/cycles", form);
    }

    private static PayableRequisitionLinkConsumer NewLinkConsumer(IServiceProvider sp) =>
        new(sp.GetRequiredService<IUnitOfWork>());

    [Fact]
    public async Task Finance_consumer_creates_exactly_one_payable_idempotently()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"fin_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");

        var requisitionId = Guid.NewGuid();
        var msg = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Compra de cadeiras", "2026-07", "Product", new DateOnly(2026, 7, 15),
            [new PlannedInstallment(1, new DateOnly(2026, 7, 10), 1_000m, "Pix")], []);

        // First delivery.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = NewApprovalConsumer(scope.ServiceProvider);
            await consumer.ProcessAsync(msg, default);
        }

        // Redelivery (at-least-once) — must not create a second Payable.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = NewApprovalConsumer(scope.ServiceProvider);
            await consumer.ProcessAsync(msg, default);
        }

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payables = await db.Set<Payable>().Include(p => p.Installments)
            .Where(p => p.OriginRequisitionId == requisitionId).ToListAsync();
        var payable = Assert.Single(payables);
        Assert.Equal(PayableOrigin.Purchase, payable.Origin);
        Assert.Equal(Raw(companyId), payable.CompanyId);
        Assert.Equal(Raw(vendorId), payable.VendorId);
        Assert.Equal(Raw(unitId), payable.OriginUnitId);
        Assert.Equal(1_000m, payable.TotalAmount);
        Assert.Equal(new DateOnly(2026, 7, 15), payable.IssueDate); // authored emission date (AEL-844) overrides the competency default
        Assert.Equal(PaymentMethod.Pix, Assert.Single(payable.Installments).PaymentMethod); // forma de pagamento carried per parcela (AEL-844)
        Assert.Equal(PayableType.Product, payable.Type); // nature carried from the requisition type (Purchase → Product)
    }

    [Fact]
    public async Task Finance_consumer_creates_the_payable_for_a_vendor_registered_under_any_company()
    {
        // Vendors are global to the organization: a requisition in company A can name any vendor, even with a
        // second company (B) present in the org. Pre-fix the consumer filtered the vendor by company and THREW on
        // a mismatch → MassTransit redelivered → DLQ, and the Payable was silently never created. Guards that the
        // vendor is resolved by id alone (create + re-approval share the one lookup).
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"xco_{Guid.NewGuid():N}@ulbra.br");

        var companyA = await FinanceSeed.CreateCompanyAsync(client);
        var unitA = await FinanceSeed.CreateUnitAsync(client, companyA, "Campus A", "CA");
        var ccA = await FinanceSeed.CreateCostCenterAsync(client, companyA, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");
        var companyB = await FinanceSeed.CreateCompanyAsync(client); // a second company in the org
        var globalVendor = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Global");

        var requisitionId = Guid.NewGuid();
        var msg = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyA), Raw(unitA), Raw(ccA), Raw(globalVendor),
            "Compra cross-empresa", "2026-07", "Product", new DateOnly(2026, 7, 15),
            [new PlannedInstallment(1, new DateOnly(2026, 7, 10), 1_000m, "Pix")], []);

        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(msg, default);

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payable = await db.Set<Payable>().SingleAsync(p => p.OriginRequisitionId == requisitionId);
        Assert.Equal(Raw(companyA), payable.CompanyId);          // the Payable belongs to company A
        Assert.Equal(Raw(globalVendor), payable.VendorId);        // the global vendor resolved by id, no company scoping
        Assert.Equal("Fornecedor Global", payable.CounterpartyName); // …and its name was actually read (not a null vendor)
    }

    [Fact]
    public async Task Returning_a_requisition_payable_records_the_transition_and_reason_on_the_requisition()
    {
        // AEL-935: the whole point of the return — the requisition detail (GET) must carry the ReturnFromPayment
        // transition WITH its reason, so the Solicitante/Controladoria see the histórico entry + the reason banner.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"ret_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var ccRes = await client.PostAsJsonAsync("/api/cost-centers", new
        {
            companyId,
            code = $"CC{Random.Shared.Next(100_000, 999_999)}",
            name = "Marketing",
            autoApprovalLimit = 0m,
            unitIds = new[] { unitId }, // required for requisition create
        });
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        {
            unitId,
            startCompetency = "2026-07",
            endCompetency = "2026-12",
            monthlyLimit = 1_000_000m,
        });

        // Front-loaded plan → ApproveTriage goes straight to Approval; then approve → InPayment.
        var createRes = await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Comprar cadeiras",
            observation = (string?)null,
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
            issueDate = "2026-07-05",
            installments = new object[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        var reqTypedId = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        var approved = await ReadJson(await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }));
        Assert.Equal("InPayment", approved.GetProperty("status").GetString());

        // Finance returns the title → drive the Budgeting consumer directly (broker substituted, as elsewhere here).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = new PayableRejectedConsumer(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            await consumer.ProcessAsync(new PayableRejected(
                Guid.NewGuid(), Raw(companyId), Raw(reqTypedId), "Nota fiscal divergente do pedido.",
                Guid.NewGuid().ToString(), "Requester"), default);
        }

        // The requisition detail must show the return: status Complemento + the transition carrying the reason.
        var detail = await ReadJson(await client.GetAsync($"/api/requisitions/{reqTypedId}"));
        Assert.Equal("Complement", detail.GetProperty("status").GetString());
        var last = detail.GetProperty("transitions").EnumerateArray().Last();
        Assert.Equal("ReturnFromPayment", last.GetProperty("action").GetString());
        Assert.Equal("Nota fiscal divergente do pedido.", last.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Refreshing_a_returned_payable_on_reapproval_keeps_its_audit_history()
    {
        // AEL-935: the refresh (Reopen + re-hydrate) reuses the SAME payable and must NOT erase its audit trail.
        // The return event is durable (sourced from the append-only audit log), so it survives Reopen clearing
        // RejectedAt on the Approver's re-approval. This is the exact "não pode apagar o histórico" guarantee.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"aud_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");

        var requisitionId = Guid.NewGuid();
        var msg = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Compra de cadeiras", "2026-07", "Product", new DateOnly(2026, 7, 15),
            [new PlannedInstallment(1, new DateOnly(2026, 7, 10), 1_000m, "Pix")], []);

        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(msg, default);

        Guid payableId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            payableId = (await db.Set<Payable>().FirstAsync(x => x.OriginRequisitionId == requisitionId)).Id;
        }

        // Finance returns the title through the REAL endpoint (byte-identical to the live path) → the reject is
        // audited (RejectedAt set) via the interceptor, exactly as it happens in production.
        var returnRes = await client.PostAsJsonAsync($"/api/payables/payable_{payableId:N}/return",
            new { reason = "NF divergente", returnTo = "Requester" });
        returnRes.EnsureSuccessStatusCode();

        // Approver re-approves the corrected requisition → the same payable is reopened + re-hydrated (RejectedAt cleared).
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(msg, default);

        await using (var verify = factory.Services.CreateAsyncScope())
        {
            var vdb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            var refreshed = await vdb.Set<Payable>().FirstAsync(x => x.Id == payableId);
            Assert.Equal(PayableStatus.Received, refreshed.Status); // reopened
            Assert.Null(refreshed.RejectionReason); // current field cleared by Reopen …
        }

        // … yet the audit trail still carries the return (durable) — the history was NOT erased by the refresh.
        var trail = await ReadJson(await client.GetAsync($"/api/payables/payable_{payableId:N}/audit-trail"));
        var types = trail.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("Returned", types);
        Assert.Contains("Reopened", types);
        // Requisition-sourced → the timeline opens with "Requisição aprovada" (AEL-948), not a generic "Título criado".
        Assert.Contains("RequisitionApproved", types);
        Assert.DoesNotContain("Created", types);
    }

    [Fact]
    public async Task Reapproving_a_rejected_requisition_refreshes_the_same_payable_in_place()
    {
        // AEL-935: a rejected requisition-sourced payable is returned to Budgeting; when the corrected requisition
        // is re-approved, the consumer refreshes the SAME title in place (same PayableId, new data) — it does not
        // create a second one (the partial unique index on OriginRequisitionId still holds the slot).
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"rej_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");

        var requisitionId = Guid.NewGuid();
        var msg = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Compra de cadeiras", "2026-07", "Product", new DateOnly(2026, 7, 15),
            [new PlannedInstallment(1, new DateOnly(2026, 7, 10), 1_000m, "Pix")], []);

        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(msg, default);

        // Finance rejects the title (the requisition is returned + corrected elsewhere).
        Guid payableId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Set<Payable>().FirstAsync(x => x.OriginRequisitionId == requisitionId);
            payableId = p.Id;
            p.Reject("NF divergente", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        // Re-approval carries the corrected data (new value + due date).
        var corrected = msg with
        {
            Description = "Compra de cadeiras (corrigida)",
            Installments = [new PlannedInstallment(1, new DateOnly(2026, 8, 10), 1_200m, "Pix")],
        };
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(corrected, default);

        await using var verify = factory.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payables = await vdb.Set<Payable>().Include(p => p.Installments)
            .Where(p => p.OriginRequisitionId == requisitionId).ToListAsync();
        var refreshed = Assert.Single(payables); // same title, not a second one
        Assert.Equal(payableId, refreshed.Id);
        Assert.Equal(PayableStatus.Received, refreshed.Status); // reopened, back in the pipeline
        Assert.Equal(1_200m, refreshed.TotalAmount); // corrected data applied in place
        Assert.Equal("Compra de cadeiras (corrigida)", refreshed.Description);
        Assert.Equal(new DateOnly(2026, 8, 10), Assert.Single(refreshed.Installments).DueDate);
    }

    [Fact]
    public async Task Recurring_requisition_allows_base_plus_cycle_payables_and_keeps_base_idempotent()
    {
        // AEL-877: a recurring requisition maps to N payables — one base (from approval, null cycle) + one per
        // monthly cycle ("YYYY-MM"). The partial unique indexes allow both to coexist yet still block a second base.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"rec_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        var requisitionId = Guid.NewGuid();

        var baseMsg = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Conta de luz (recorrente)", "2026-07", "Service", new DateOnly(2026, 7, 5),
            [new PlannedInstallment(1, new DateOnly(2026, 7, 10), 1_000m)], []);
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(baseMsg, default);

        // A cycle payable for 2026-08 (the cycle consumer arrives in A4; here we insert it directly to exercise
        // the schema — base and cycle must coexist).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cycle = new Payable(Raw(companyId), PayableOrigin.Purchase, Raw(vendorId), "Fornecedor Teste",
                documentNumber: null, new DateOnly(2026, 8, 5), "Conta de luz 08", Raw(costCenterId), categoryId: null,
                [new Installment(1, new DateOnly(2026, 8, 10), 900m)]);
            cycle.SetOrigin(requisitionId, Raw(unitId));
            cycle.SetCycle("2026-08");
            db.Set<Payable>().Add(cycle);
            await db.SaveChangesAsync();
        }

        // Redelivering the base is still idempotent (no second base).
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(baseMsg, default);

        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payables = await verifyDb.Set<Payable>().Where(p => p.OriginRequisitionId == requisitionId).ToListAsync();
        Assert.Equal(2, payables.Count); // base + cycle
        Assert.Single(payables, p => p.OriginCycleCompetency == null); // exactly one base
        Assert.Single(payables, p => p.OriginCycleCompetency == "2026-08"); // the cycle
    }

    [Fact]
    public async Task Payable_link_callback_records_base_and_cycle_payables_on_the_requisition()
    {
        // AEL-877 (return link): after Finance creates the base/cycle Payables it tells Budgeting, which stamps
        // Requisition.BasePayableId and RequisitionCycle.PayableId. Idempotent (first-wins).
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"link_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-12", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "FixedExpense", description = "Conta de luz", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        var reqId = Raw(reqTypedId);
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        // Launch a cycle directly on the aggregate (the endpoint arrives in A5).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var req = await uow.Set<Requisition>().Include(r => r.Cycles).Include(r => r.Transitions).FirstAsync(r => r.Id == reqId);
            var cycle = req.AddCycle("2026-08", 900m, new DateOnly(2026, 8, 10), PaymentMethod.Pix, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await uow.AddAsync(cycle, default);
            await uow.AddAsync(req.Transitions[^1], default);
            await uow.SaveChangesAsync(default);
        }

        var basePayableId = Guid.NewGuid();
        var cyclePayableId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewLinkConsumer(scope.ServiceProvider).ProcessAsync(new PayableCreatedForRequisition(reqId, basePayableId, null, Raw(companyId)), default);
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewLinkConsumer(scope.ServiceProvider).ProcessAsync(new PayableCreatedForRequisition(reqId, cyclePayableId, "2026-08", Raw(companyId)), default);
        // Redelivery of the base with a different id — first-wins, must not overwrite.
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewLinkConsumer(scope.ServiceProvider).ProcessAsync(new PayableCreatedForRequisition(reqId, Guid.NewGuid(), null, Raw(companyId)), default);

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Set<Requisition>().Include(r => r.Cycles).FirstAsync(r => r.Id == reqId);
        Assert.Equal(basePayableId, saved.BasePayableId); // base linked, redelivery didn't overwrite
        Assert.Equal(cyclePayableId, Assert.Single(saved.Cycles).PayableId);
    }

    [Fact]
    public async Task Cancelled_cycle_payable_backs_out_the_cycle_and_blocks_redelivery_resurrection()
    {
        // P2 (AEL-877): cancelling a cycle's Payable removes the RequisitionCycle (stops counting toward
        // settlement, frees the competency) and a redelivered cycle message must NOT resurrect the payable.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"cxl_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-12", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "FixedExpense", description = "Conta de luz", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        var reqId = Raw(reqTypedId);
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        // Launch the August cycle via the endpoint (creates the row + publishes) and materialize its Payable.
        (await LaunchCycleAsync(client, reqTypedId, "2026-08", "2026-08-10", 900m)).EnsureSuccessStatusCode();
        var cycleMsg = new RequisitionCycleAddedForPayment(
            reqId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Conta de luz", "2026-08", "Service", new PlannedInstallment(1, new DateOnly(2026, 8, 10), 900m, "Pix"));
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewCycleConsumer(scope.ServiceProvider).ProcessAsync(cycleMsg, default);

        // Cancel the cycle's Payable in Finance, then run the return path.
        Guid cyclePayableId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payable = await db.Set<Payable>().SingleAsync(p => p.OriginRequisitionId == reqId && p.OriginCycleCompetency == "2026-08");
            payable.Cancel("cancelado no Financeiro");
            cyclePayableId = payable.Id;
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = new CyclePayableCancelledConsumer(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            await consumer.ProcessAsync(new PayableCancelled(cyclePayableId, Raw(companyId)), default);
        }

        // Redelivered cycle message must not resurrect the payable (the cycle row is gone).
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewCycleConsumer(scope.ServiceProvider).ProcessAsync(cycleMsg, default);

        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = await verifyDb.Set<Ulbra.One.Budgeting.Domain.Entities.Requisition>()
            .Include(r => r.Cycles).SingleAsync(r => r.Id == reqId);
        Assert.Empty(req.Cycles); // backed out
        var cyclePayables = await verifyDb.Set<Payable>()
            .Where(p => p.OriginRequisitionId == reqId && p.OriginCycleCompetency == "2026-08").ToListAsync();
        var only = Assert.Single(cyclePayables); // only the cancelled one — no resurrection
        Assert.Equal(PayableStatus.Cancelled, only.Status);
    }

    [Fact]
    public async Task Relaunching_a_cancelled_cycle_creates_a_fresh_payable_alongside_the_cancelled_one()
    {
        // AEL-877 (3.3): the full cross-consumer path cancel → back-out → relaunch. After a cycle's Payable is
        // cancelled and CyclePayableCancelledConsumer frees the competency, a NEW AddRecurrenceCycle for the SAME
        // competency materializes a fresh Payable — the cycle consumer's dedup and the partial unique index both
        // ignore the Cancelled row (status <> 'Cancelled'), so the two coexist.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"relaunch_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-12", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "FixedExpense", description = "Conta de luz", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        var reqId = Raw(reqTypedId);
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        // Launch the August cycle (amount 900) and materialize its Payable.
        (await LaunchCycleAsync(client, reqTypedId, "2026-08", "2026-08-10", 900m)).EnsureSuccessStatusCode();
        var firstMsg = new RequisitionCycleAddedForPayment(
            reqId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Conta de luz", "2026-08", "Service", new PlannedInstallment(1, new DateOnly(2026, 8, 10), 900m, "Pix"));
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewCycleConsumer(scope.ServiceProvider).ProcessAsync(firstMsg, default);

        // Cancel that Payable and run the return path → the cycle row is backed out, the competency freed.
        Guid firstPayableId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payable = await db.Set<Payable>().SingleAsync(p => p.OriginRequisitionId == reqId && p.OriginCycleCompetency == "2026-08");
            payable.Cancel("cancelado no Financeiro");
            firstPayableId = payable.Id;
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = new CyclePayableCancelledConsumer(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            await consumer.ProcessAsync(new PayableCancelled(firstPayableId, Raw(companyId)), default);
        }

        // Relaunch August with a DIFFERENT amount (950) — the freed competency accepts it, and the cycle consumer
        // creates a brand-new Payable because the only existing 2026-08 row is Cancelled.
        (await LaunchCycleAsync(client, reqTypedId, "2026-08", "2026-08-12", 950m)).EnsureSuccessStatusCode();
        var relaunchMsg = new RequisitionCycleAddedForPayment(
            reqId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Conta de luz", "2026-08", "Service", new PlannedInstallment(1, new DateOnly(2026, 8, 12), 950m, "Pix"), Code: 8);
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewCycleConsumer(scope.ServiceProvider).ProcessAsync(relaunchMsg, default);

        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var cyclePayables = await verifyDb.Set<Payable>().Include(p => p.Installments)
            .Where(p => p.OriginRequisitionId == reqId && p.OriginCycleCompetency == "2026-08").ToListAsync();
        Assert.Equal(2, cyclePayables.Count); // the cancelled 900 + the fresh 950 coexist
        var cancelled = Assert.Single(cyclePayables, p => p.Status == PayableStatus.Cancelled);
        var live = Assert.Single(cyclePayables, p => p.Status != PayableStatus.Cancelled);
        Assert.Equal(firstPayableId, cancelled.Id);
        Assert.Equal(950m, live.TotalAmount); // the relaunch, not a resurrection of the 900

        // The requisition holds exactly one (live) cycle again for that competency.
        var req = await verifyDb.Set<Requisition>().Include(r => r.Cycles).SingleAsync(r => r.Id == reqId);
        var cycle = Assert.Single(req.Cycles);
        Assert.Equal("2026-08", cycle.Competency);
        Assert.Equal(950m, cycle.Amount);
    }

    [Fact]
    public async Task Launching_a_cycle_with_an_nf_file_attaches_it_to_the_cycle_payable_not_the_general_list()
    {
        // AEL-877: the NF uploaded in the "Lançar fatura do mês" form rides the cycle message and is copied onto
        // the cycle's Payable — never onto the general requisition attachments. The launch endpoint stores the
        // blob; the consumer copies it onto the Payable it materializes.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"cycnf_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-12", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "FixedExpense", description = "Conta de luz", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        var reqId = Raw(reqTypedId);
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        // Launch August WITH an NF file (PDF) — the endpoint stores it and rides the ref on the cycle message.
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 nota fiscal do ciclo");
        (await LaunchCycleAsync(client, reqTypedId, "2026-08", "2026-08-10", 900m, file: (pdf, "nf-agosto.pdf", "application/pdf")))
            .EnsureSuccessStatusCode();

        // The file did NOT become a general requisition attachment.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Set<RequisitionAttachment>().Where(a => a.RequisitionId == reqId).ToListAsync());
        }

        // Drive the cycle consumer with the message carrying the NF ref (a real stored blob) — it materializes the
        // Payable and copies the NF onto it. Mirrors the outbox message the endpoint published.
        string nfKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            using var ms = new MemoryStream(pdf);
            nfKey = await scope.ServiceProvider.GetRequiredService<IFileStorageService>().SaveAsync("nf-agosto.pdf", ms);
        }
        var cycleMsg = new RequisitionCycleAddedForPayment(
            reqId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Conta de luz", "2026-08", "Service", new PlannedInstallment(1, new DateOnly(2026, 8, 10), 900m, "Pix"),
            Code: null, RequiresFiscalDocument: true,
            Attachment: new RequisitionAttachmentRef("nf-agosto.pdf", "application/pdf", pdf.Length, nfKey, Guid.NewGuid(), "FiscalDocument"));
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewCycleConsumer(scope.ServiceProvider).ProcessAsync(cycleMsg, default);

        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payable = await verifyDb.Set<Payable>().Include(p => p.Attachments)
            .SingleAsync(p => p.OriginRequisitionId == reqId && p.OriginCycleCompetency == "2026-08");
        var att = Assert.Single(payable.Attachments); // NF copied onto THE cycle's Payable
        Assert.Equal("nf-agosto.pdf", att.FileName);
        Assert.NotEqual(nfKey, att.StoragePath); // independent Finance-owned copy
        Assert.Equal(PayableAttachmentKind.FiscalDocument, att.Kind); // Kind carried across the boundary (AEL-959)
    }

    [Fact]
    public async Task Cycle_consumer_materializes_an_extra_payable_idempotently()
    {
        // AEL-877: launching a monthly cycle creates ONE more Payable, tagged with its competency, riding the
        // normal Finance pipeline. Redelivery (at-least-once) must not create a second cycle Payable. The
        // requisition and its cycle row are REAL (the consumer refuses to materialize a cycle the requisition
        // doesn't have — the resurrection guard).
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"cyc_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-12", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "FixedExpense", description = "Conta de luz (recorrente)", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        var requisitionId = Raw(reqTypedId);
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        // The August invoice, paid by boleto (convênio) — the endpoint records the cycle row + publishes.
        var cycleLine = FinanceSeed.NewConvenioLine();
        (await LaunchCycleAsync(client, reqTypedId, "2026-08", "2026-08-10", 900m, "Boleto", cycleLine)).EnsureSuccessStatusCode();

        var cycleMsg = new RequisitionCycleAddedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Conta de luz (recorrente)", "2026-08", "Service",
            new PlannedInstallment(1, new DateOnly(2026, 8, 10), 900m, "Boleto", cycleLine),
            Code: 7);

        for (var i = 0; i < 2; i++) // deliver twice — idempotent on (requisition, competency)
            await using (var scope = factory.Services.CreateAsyncScope())
                await NewCycleConsumer(scope.ServiceProvider).ProcessAsync(cycleMsg, default);

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payables = await db.Set<Payable>().Include(p => p.Installments)
            .Where(p => p.OriginRequisitionId == requisitionId && p.OriginCycleCompetency == "2026-08").ToListAsync();
        var cycle = Assert.Single(payables); // one cycle Payable (redelivery no-op)
        Assert.Equal(900m, cycle.TotalAmount);
        Assert.Equal(cycleLine, Assert.Single(cycle.Installments).BoletoDigitableLine);
    }

    [Fact]
    public async Task Finance_consumer_materializes_authored_installments_cent_exact()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"fin_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");

        var requisitionId = Guid.NewGuid();
        // Uneven split that only ties out to 100.00 with remainder-on-last (33.33 / 33.33 / 33.34) — proves the
        // consumer copies the authored per-installment amounts verbatim (no re-division, no drift).
        var msg = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Serviço parcelado", "2026-07", "Service", new DateOnly(2026, 7, 15),
            [
                new PlannedInstallment(1, new DateOnly(2026, 8, 1), 33.33m, "Pix"),
                new PlannedInstallment(2, new DateOnly(2026, 9, 1), 33.33m, "Boleto", "23793381286008271309005678911101234560000010000"),
                new PlannedInstallment(3, new DateOnly(2026, 10, 1), 33.34m, "Ted"),
            ], []);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = NewApprovalConsumer(scope.ServiceProvider);
            await consumer.ProcessAsync(msg, default);
        }

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payable = await db.Set<Payable>().Include(p => p.Installments)
            .SingleAsync(p => p.OriginRequisitionId == requisitionId);

        Assert.Equal(3, payable.Installments.Count);
        Assert.Equal(100.00m, payable.TotalAmount); // Σ authored amounts, cent-exact
        var ordered = payable.Installments.OrderBy(i => i.Sequence).ToList();
        Assert.Equal((33.33m, new DateOnly(2026, 8, 1), PaymentMethod.Pix), (ordered[0].Amount, ordered[0].DueDate, ordered[0].PaymentMethod));
        Assert.Equal((33.33m, new DateOnly(2026, 9, 1), PaymentMethod.Boleto), (ordered[1].Amount, ordered[1].DueDate, ordered[1].PaymentMethod));
        Assert.Equal((33.34m, new DateOnly(2026, 10, 1), PaymentMethod.Ted), (ordered[2].Amount, ordered[2].DueDate, ordered[2].PaymentMethod));
        // AEL-877: the boleto linha digitável travels from the outbox message to the Payable installment (Boleto only).
        Assert.Equal("23793381286008271309005678911101234560000010000", ordered[1].BoletoDigitableLine);
        Assert.Null(ordered[0].BoletoDigitableLine);
        Assert.Null(ordered[2].BoletoDigitableLine);
    }

    [Fact]
    public async Task Budgeting_consumer_folds_payment_into_realizado_and_marks_paid()
    {
        var factory = fixture.Api;
        var email = $"auto_{Guid.NewGuid():N}@ulbra.br";
        var client = await factory.CreateAdminClientAsync(email); // admin: has triage + approve permissions

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        // A unidade precisa estar associada ao CC — o create de requisição exige cobertura (AEL-830).
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        // Orçamento p/ a competência 2026-07 — sem célula, o guard (AEL-796) barraria a aprovação.
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-07", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "Purchase",
            description = "Comprar cadeiras",
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        // Drive the full esteira to InPayment (AEL-841): submit → approve-triage → financial complement → approve.
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        var payment = new PaymentRegistered(
            Guid.NewGuid(), Guid.NewGuid(), Raw(companyId), 1_000m, new DateOnly(2026, 7, 10),
            null, [], RequisitionId: Raw(reqTypedId), Competency: "2026-07");

        // Apply twice — idempotent on PaymentId.
        for (var i = 0; i < 2; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var consumer = new RequisitionPaymentConsumer(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>(),
                scope.ServiceProvider.GetRequiredService<ILogger<RequisitionPaymentConsumer>>());
            await consumer.ApplyPaymentAsync(payment, default);
        }

        var after = await ReadJson(await client.GetAsync($"/api/requisitions/{reqTypedId}"));
        Assert.Equal("Paid", after.GetProperty("status").GetString());
        Assert.Equal(1_000m, after.GetProperty("realizedValue").GetDecimal());
    }

    /// <summary>AEL-884: the cost-center list card shows executado (realizado). The /cost-center-realized endpoint
    /// must SUM the requisition payments per cost center — a regression guard for the bug where a correlated Sum
    /// in a projection returned 0 for every cost center (card stuck at 0% while a paid requisition existed).</summary>
    [Fact]
    public async Task Cost_center_realized_endpoint_sums_the_requisition_payments()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"ccreal_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "TI", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-07", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "Purchase", description = "Colocation de servidores", estimateValue = 26_700m,
            estimateType = "Full", startCompetency = "2026-07", firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 26_700m, paymentMethod = "Pix" } },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        // Settlement: fold a registered payment back into the requisition's realized total.
        var payment = new PaymentRegistered(
            Guid.NewGuid(), Guid.NewGuid(), Raw(companyId), 26_700m, new DateOnly(2026, 7, 10),
            null, [], RequisitionId: Raw(reqTypedId), Competency: "2026-07");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = new RequisitionPaymentConsumer(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>(),
                scope.ServiceProvider.GetRequiredService<ILogger<RequisitionPaymentConsumer>>());
            await consumer.ApplyPaymentAsync(payment, default);
        }

        // The card endpoint sums the payment onto the cost center (would be empty under the projection bug).
        var realized = await ReadJson(await client.GetAsync($"/api/requisitions/cost-center-realized?companyId={companyId}"));
        var line = realized.EnumerateArray().Single(l => l.GetProperty("costCenterId").GetString() == costCenterId);
        Assert.Equal(26_700m, line.GetProperty("realized").GetDecimal());
    }

    /// <summary>AEL-883: /cost-center-realized is recortado by the caller's scope. A scoped Requester only sees
    /// the realizado of its own natureza/unit; an unscoped role (Admin) still sees every cost center.</summary>
    [Fact]
    public async Task Cost_center_realized_is_scoped_to_the_callers_units_and_naturezas()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"ccrealscope_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var inUnit = await FinanceSeed.CreateUnitAsync(admin, companyId, "In Scope", "IN");
        var inCc = await FinanceSeed.CreateCostCenterAsync(admin, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "In", inUnit);
        var outUnit = await FinanceSeed.CreateUnitAsync(admin, companyId, "Out Of Scope", "OUT");
        var outCc = await FinanceSeed.CreateCostCenterAsync(admin, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Out", outUnit);
        var vendorId = await FinanceSeed.CreateVendorAsync(admin, "Fornecedor Teste");
        foreach (var (u, c) in new[] { (inUnit, inCc), (outUnit, outCc) })
            await admin.PostAsJsonAsync($"/api/cost-centers/{c}/unit-budgets", new
            { unitId = u, startCompetency = "2026-07", endCompetency = "2026-07", monthlyLimit = 1_000_000m });

        // One paid requisition in each dimension → realizado on both cost centers.
        await PayRequisitionAsync(factory, admin, companyId, inUnit, inCc, vendorId, 10_000m);
        await PayRequisitionAsync(factory, admin, companyId, outUnit, outCc, vendorId, 20_000m);

        // Admin (unscoped) sees both realizados.
        var adminRows = await ReadJson(await admin.GetAsync($"/api/requisitions/cost-center-realized?companyId={companyId}"));
        Assert.Equal(10_000m, adminRows.EnumerateArray().Single(l => l.GetProperty("costCenterId").GetString() == inCc).GetProperty("realized").GetDecimal());
        Assert.Equal(20_000m, adminRows.EnumerateArray().Single(l => l.GetProperty("costCenterId").GetString() == outCc).GetProperty("realized").GetDecimal());

        // Requester scoped to (inUnit, inCc) sees only its realizado.
        var requester = factory.CreateClientWithUnitScope(
            Roles.Requester,
            [CompanyId.Parse(companyId, null).Value],
            [(CompanyId.Parse(companyId, null).Value, UnitId.Parse(inUnit, null).Value)],
            [(CompanyId.Parse(companyId, null).Value, CostCenterId.Parse(inCc, null).Value)],
            Permissions.RequisitionsRead);
        var scopedRows = await ReadJson(await requester.GetAsync($"/api/requisitions/cost-center-realized?companyId={companyId}"));
        var only = Assert.Single(scopedRows.EnumerateArray().ToList());
        Assert.Equal(inCc, only.GetProperty("costCenterId").GetString());
        Assert.Equal(10_000m, only.GetProperty("realized").GetDecimal());
    }

    /// <summary>Drives a requisition through the esteira to InPayment and folds a registered payment back into
    /// its realizado (via the consumer core, no broker) — the minimum to give a cost center realized spend.</summary>
    private static async Task PayRequisitionAsync(
        ApiFactory factory, HttpClient admin, string companyId, string unitId, string costCenterId, string vendorId, decimal amount)
    {
        var created = await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "Purchase", description = "Item", estimateValue = amount,
            estimateType = "Full", startCompetency = "2026-07", firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        await admin.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await admin.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await admin.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount, paymentMethod = "Pix" } },
        });
        await admin.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        var payment = new PaymentRegistered(
            Guid.NewGuid(), Guid.NewGuid(), Raw(companyId), amount, new DateOnly(2026, 7, 10),
            null, [], RequisitionId: Raw(reqTypedId), Competency: "2026-07");
        await using var scope = factory.Services.CreateAsyncScope();
        var consumer = new RequisitionPaymentConsumer(
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            scope.ServiceProvider.GetRequiredService<ILogger<RequisitionPaymentConsumer>>());
        await consumer.ApplyPaymentAsync(payment, default);
    }

    /// <summary>Regression (AEL-844): the authored per-parcela plan (distinct due dates, amounts and forma de
    /// pagamento) must survive from the requisition to the published <c>RequisitionApprovedForPayment</c>. The
    /// pre-fix approve endpoint did not Include the installments, so the event fell back to a single uniform
    /// installment with method "Other" — this asserts the real published outbox message carries the 3 parcelas.</summary>
    [Fact]
    public async Task Approve_publishes_authored_installment_plan_to_finance()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"plan_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-12", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "Purchase",
            description = "Comprar cadeiras",
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            firstDueDate = "2026-07-10",
            paymentInstallmentsCount = (int?)null, // no plan → the buggy fallback would emit ONE installment
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        // Author 3 parcelas with distinct dates/amounts/methods (Σ = 1000 = aprovado → não volta p/ triagem).
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-15",
            installments = new[]
            {
                new { dueDate = "2026-08-01", amount = 300m, paymentMethod = "Pix", boletoDigitableLine = (string?)null },
                new { dueDate = "2026-09-15", amount = 300m, paymentMethod = "Boleto", boletoDigitableLine = (string?)FinanceSeed.NewBoletoLine() },
                new { dueDate = "2026-10-20", amount = 400m, paymentMethod = "Ted", boletoDigitableLine = (string?)null },
            },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment, publishes the event

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        // Read the real published message from the EF bus outbox (broker is unreachable in tests → row persists).
        var bodies = await db.Database
            .SqlQueryRaw<string>("SELECT body::text AS \"Value\" FROM masstransit.outbox_message WHERE message_type LIKE '%RequisitionApprovedForPayment%'")
            .ToListAsync();
        var body = Assert.Single(bodies, b => b.Contains(Raw(reqTypedId).ToString()));
        // The authored plan survived: distinct due dates + per-parcela methods that the fallback never produces.
        Assert.Contains("2026-08-01", body);
        Assert.Contains("2026-09-15", body);
        Assert.Contains("2026-10-20", body);
        Assert.Contains("Boleto", body);
        Assert.Contains("Ted", body);
    }

    [Fact]
    public async Task Finance_consumer_copies_requisition_attachments_onto_the_payable()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"att_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");

        // Seed the requisition's attachment blob in storage (as the upload endpoint would have).
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 orcamento anexo");
        string sourceKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            using var ms = new MemoryStream(pdf);
            sourceKey = await scope.ServiceProvider.GetRequiredService<IFileStorageService>().SaveAsync("orcamento.pdf", ms);
        }

        var requisitionId = Guid.NewGuid();
        var msg = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Compra com anexo", "2026-07", "Product", null,
            [new PlannedInstallment(1, new DateOnly(2026, 7, 10), 1_000m)],
            [new RequisitionAttachmentRef("orcamento.pdf", "application/pdf", pdf.Length, sourceKey, Guid.NewGuid())]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = NewApprovalConsumer(scope.ServiceProvider);
            await consumer.ProcessAsync(msg, default);
        }

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payable = await db.Set<Payable>().Include(p => p.Attachments)
            .SingleAsync(p => p.OriginRequisitionId == requisitionId);

        var att = Assert.Single(payable.Attachments);
        Assert.Equal("orcamento.pdf", att.FileName);
        Assert.Equal("application/pdf", att.ContentType);
        Assert.Equal(pdf.Length, att.SizeBytes);
        Assert.Equal(PayableAttachmentKind.Other, att.Kind); // no kind on the ref → defaults to Other (AEL-959)
        Assert.NotEqual(sourceKey, att.StoragePath); // copied to an independent Finance-owned key

        // The copied blob holds the same bytes as the requisition's original.
        await using var copied = await verify.ServiceProvider.GetRequiredService<IFileStorageService>().GetAsync(att.StoragePath);
        Assert.NotNull(copied);
        using var read = new MemoryStream();
        await copied!.CopyToAsync(read);
        Assert.Equal(pdf, read.ToArray());
    }

    /// <summary>AEL-865 self-service NF loop: an attachment added to an already-approved requisition is carried
    /// onto its existing Payable, so it leaves the "NF pendente" bucket. Redelivery is idempotent (no duplicate).</summary>
    [Fact]
    public async Task Post_approval_attachment_propagates_onto_the_payable_and_clears_the_nf_pendency()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"prop_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");

        // Materialize a Payable WITHOUT attachments — it is "NF pendente" (no fiscal document, no attachment).
        var requisitionId = Guid.NewGuid();
        var approved = new RequisitionApprovedForPayment(
            requisitionId, Raw(companyId), Raw(unitId), Raw(costCenterId), Raw(vendorId),
            "Compra sem NF", "2026-07", "Product", new DateOnly(2026, 7, 15),
            [new PlannedInstallment(1, new DateOnly(2026, 7, 10), 1_000m)], []);
        await using (var scope = factory.Services.CreateAsyncScope())
            await NewApprovalConsumer(scope.ServiceProvider).ProcessAsync(approved, default);

        await using (var verifyPending = factory.Services.CreateAsyncScope())
        {
            var db = verifyPending.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.Set<Payable>().Include(p => p.Attachments)
                .SingleAsync(p => p.OriginRequisitionId == requisitionId);
            Assert.Empty(pending.Attachments); // starts in the NF-pending bucket
        }

        // The requester uploads the NF after approval → the upload endpoint publishes RequisitionAttachmentAdded.
        var nf = Encoding.ASCII.GetBytes("%PDF-1.4 nota fiscal");
        string sourceKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            using var ms = new MemoryStream(nf);
            sourceKey = await scope.ServiceProvider.GetRequiredService<IFileStorageService>().SaveAsync("nota-fiscal.pdf", ms);
        }
        var added = new RequisitionAttachmentAdded(requisitionId, Raw(companyId),
            new RequisitionAttachmentRef("nota-fiscal.pdf", "application/pdf", nf.Length, sourceKey, Guid.NewGuid(), "FiscalDocument"),
            DateTimeOffset.UtcNow);

        // Deliver twice (at-least-once) — the second must be a no-op.
        for (var i = 0; i < 2; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var consumer = new RequisitionAttachmentAddedConsumer(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<IRequisitionAttachmentCopier>(),
                scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            await consumer.ProcessAsync(added, default);
        }

        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var payable = await verifyDb.Set<Payable>().Include(p => p.Attachments)
            .SingleAsync(p => p.OriginRequisitionId == requisitionId);

        var att = Assert.Single(payable.Attachments); // propagated once, idempotent on redelivery
        Assert.Equal("nota-fiscal.pdf", att.FileName);
        Assert.NotEqual(sourceKey, att.StoragePath); // copied to an independent Finance-owned key
        Assert.Equal(PayableAttachmentKind.FiscalDocument, att.Kind); // Kind carried across the boundary (AEL-959)
        // Left the NF-pending bucket: HasFiscalDocument now requires an NF-typed attachment (type-aware gate).
        var hasNf = await verifyDb.Set<PayableAttachment>()
            .AnyAsync(a => a.PayableId == payable.Id && a.Kind == PayableAttachmentKind.FiscalDocument);
        Assert.True(hasNf);
    }

    /// <summary>AEL-959: the "NF pendente" nag is type-aware, matching the payable-side gate — a generic/boleto/
    /// quote attachment does NOT clear it, only a Nota Fiscal (Kind=FiscalDocument) or an .xml does. Holds in the
    /// detail AND the list (the list must load attachments). "Solicitar NF" moves the requisition to
    /// AwaitingFiscalDocument (idempotent under at-least-once delivery) and it stays pending until an NF arrives.</summary>
    [Fact]
    public async Task Fiscal_pending_is_type_aware_only_a_nota_fiscal_clears_it()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"nf_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        { unitId, startCompetency = "2026-07", endCompetency = "2026-07", monthlyLimit = 1_000_000m });

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "Purchase", description = "Comprar cadeiras", estimateValue = 1_000m,
            estimateType = "Full", startCompetency = "2026-07", firstDueDate = "2026-07-10",
        }));
        var reqTypedId = created.GetProperty("id").GetString()!;
        var reqId = Raw(reqTypedId);
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve-triage", new { });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        await client.PostAsJsonAsync($"/api/requisitions/{reqTypedId}/approve", new { }); // → InPayment

        async Task<bool> DetailPendingAsync() =>
            (await ReadJson(await client.GetAsync($"/api/requisitions/{reqTypedId}")))
                .GetProperty("fiscalDocumentPending").GetBoolean();
        async Task<bool> ListPendingAsync()
        {
            var listed = await ReadJson(await client.GetAsync($"/api/requisitions?companyId={companyId}"));
            return listed.GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("id").GetString() == reqTypedId)
                .GetProperty("fiscalDocumentPending").GetBoolean();
        }
        async Task UploadAsync(string fileName, string? kind)
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 " + fileName));
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(fileContent, "file", fileName);
            var url = $"/api/requisitions/{reqTypedId}/attachments" + (kind is null ? "" : $"?kind={kind}");
            (await client.PostAsync(url, form)).EnsureSuccessStatusCode();
        }

        // InPayment, nothing attached → pending (detail AND list).
        var initial = await ReadJson(await client.GetAsync($"/api/requisitions/{reqTypedId}"));
        Assert.Equal("InPayment", initial.GetProperty("status").GetString());
        Assert.True(await DetailPendingAsync());
        Assert.True(await ListPendingAsync());

        // A generic (non-NF) attachment does NOT clear the NF nag (AEL-959 — supersedes the old AEL-871 rule);
        // the LIST agrees (it loads attachments, so it isn't falsely pending nor falsely cleared).
        await UploadAsync("orcamento.pdf", kind: null); // Kind defaults to Other
        Assert.True(await DetailPendingAsync());
        Assert.True(await ListPendingAsync());

        // "Solicitar NF" → AwaitingFiscalDocument (idempotent under redelivery); still pending, no NF yet.
        var request = new PayableFiscalDocumentRequested(
            Guid.NewGuid(), Guid.NewGuid(), Raw(companyId), reqId, 0, DateTimeOffset.UtcNow);
        for (var i = 0; i < 2; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var consumer = new PayableFiscalDocumentRequestedConsumer(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<IPublishEndpoint>());
            await consumer.ProcessAsync(request, default);
        }
        var afterRequest = await ReadJson(await client.GetAsync($"/api/requisitions/{reqTypedId}"));
        Assert.Equal("AwaitingFiscalDocument", afterRequest.GetProperty("status").GetString());
        Assert.True(await DetailPendingAsync());

        // Attaching the Nota Fiscal (Kind=FiscalDocument) clears it — detail AND list.
        await UploadAsync("nota-fiscal.pdf", kind: "FiscalDocument");
        Assert.False(await DetailPendingAsync());
        Assert.False(await ListPendingAsync());
    }

    /// <summary>AEL-865: the rule is Payable-existence. An attachment added to a requisition that has NOT
    /// generated a Payable (e.g. still pre-approval) is a no-op — no error, nothing carried anywhere.</summary>
    [Fact]
    public async Task Post_approval_attachment_is_a_noop_when_the_requisition_has_no_payable()
    {
        var factory = fixture.Api;
        var requisitionId = Guid.NewGuid(); // no payable was ever created for this requisition

        var added = new RequisitionAttachmentAdded(requisitionId, Guid.NewGuid(),
            new RequisitionAttachmentRef("nota.pdf", "application/pdf", 128, "orphan-key", Guid.NewGuid()),
            DateTimeOffset.UtcNow);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = new RequisitionAttachmentAddedConsumer(
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<IRequisitionAttachmentCopier>(),
                scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            await consumer.ProcessAsync(added, default); // must not throw
        }

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<Payable>().AnyAsync(p => p.OriginRequisitionId == requisitionId));
    }
}
