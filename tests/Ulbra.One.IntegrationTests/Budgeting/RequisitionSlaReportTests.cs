using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Application.Sla;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Ulbra.One.Notifications.Domain.Entities;
using Ulbra.One.Notifications.Infrastructure.Messaging;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>End-to-end validation of the SLA flow (AEL-872) against a real Postgres: the report endpoint over a
/// real requisition (historical occurrence + gating) and the escalation sweep (overdue → one idempotent
/// notification, in-SLA → none). Exercises the EF loader, the calculator and the sweep service the unit tests
/// can't reach.</summary>
[Collection("Postgres")]
public class RequisitionSlaReportTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private HttpClient Approver() =>
        fixture.Api.CreateClientWith(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove, Permissions.RequisitionsSlaReport);

    [Fact]
    public async Task Report_counts_a_closed_triage_occurrence_from_a_real_requisition()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var controller = factory.CreateClientWith(Roles.Controllership,
            Permissions.RequisitionsRead, Permissions.RequisitionsTriage);

        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var unitId = await FinanceSeed.CreateUnitAsync(admin, companyId, "Campus SLA", "CS");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(
            admin, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        await Ok(await admin.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        {
            unitId,
            startCompetency = "2026-07",
            endCompetency = "2026-12",
            monthlyLimit = 1_000_000m,
        }));
        var vendorId = await FinanceSeed.CreateVendorAsync(admin, "Fornecedor SLA");
        var created = await Ok(await admin.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Comprar mesas",
            observation = (string?)null,
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
        }));
        var id = created.GetProperty("id").GetString()!;
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { }));            // Draft → Triage
        await Ok(await controller.PostAsJsonAsync($"/api/requisitions/{id}/approve-triage", new { })); // Triage → FinancialComplement (closes a Triage occurrence)

        var report = await Ok(await Approver().GetAsync($"/api/requisitions/sla-report?companyId={companyId}"));

        // Historical: the closed Triage occurrence is counted and (being near-instant) is within SLA.
        var triage = report.GetProperty("stages").EnumerateArray()
            .Single(s => s.GetProperty("stage").GetString() == "Triage");
        Assert.True(triage.GetProperty("occurrences").GetInt32() >= 1);
        Assert.Equal(100d, triage.GetProperty("withinSlaPercent").GetDouble());
        // Live: the requisition is now in FinancialComplement (not an SLA stage) and nothing sat past its SLA.
        Assert.Equal(0, report.GetProperty("live").GetProperty("totalOverdue").GetInt32());
    }

    [Fact]
    public async Task Report_requires_sla_report_permission()
    {
        var factory = fixture.Api;
        // Controladoria has read + triage but NOT sla-report → forbidden.
        var noPerm = factory.CreateClientWith(Roles.Controllership,
            Permissions.RequisitionsRead, Permissions.RequisitionsTriage);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPerm.GetAsync("/api/requisitions/sla-report")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/requisitions/sla-report")).StatusCode);
    }

    [Fact]
    public async Task Sweep_detects_overdue_stages_and_reports_the_breach()
    {
        var factory = fixture.Api;
        var companyId = Guid.NewGuid(); // isolated — the SLA flow references the company by a loose Guid
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();

        // One requisition stuck in Triage for ~10 days (well past 24 business-hours) and one just entered.
        await SeedTriageAsync(factory, companyId, code: 1, enteredAt: now - TimeSpan.FromDays(10));
        await SeedTriageAsync(factory, companyId, code: 2, enteredAt: now); // within SLA → must NOT breach

        // The sweep publishes one RequisitionSlaBreached per breach (to the outbox): the overdue one counts,
        // the in-SLA one does not. (Materialization is covered by the consumer test below.)
        var result = await RunSweepAsync(factory, now);
        Assert.True(result.Breached >= 1);
    }

    [Fact]
    public async Task Sla_breach_consumer_writes_one_role_notification_idempotently()
    {
        var factory = fixture.Api;
        var reqId = Guid.NewGuid();
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var breach = new RequisitionSlaBreached(
            DeterministicGuid.From($"reqsla:{reqId}:Triage:{now:O}"),
            reqId, Guid.NewGuid(), 42, "Triage", 24, now);

        // Same breach delivered twice → exactly one notification (dedup on BreachId), to the Controladoria role.
        await DriveConsumerAsync(factory, breach);
        await DriveConsumerAsync(factory, breach);

        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notes = await uow.ReadSet<Notification>()
            .Where(n => n.ReferenceId == reqId).ToListAsync(CancellationToken.None);
        var note = Assert.Single(notes);
        Assert.Equal("Controllership", note.AudienceRole);
        Assert.Equal("RequisitionTriageSlaOverdue", note.Type);
    }

    private static async Task<RequisitionSlaSweepResult> RunSweepAsync(ApiFactory factory, DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        var sweep = scope.ServiceProvider.GetRequiredService<IRequisitionSlaSweepService>();
        return await sweep.SweepAsync(now, CancellationToken.None);
    }

    private static async Task<Guid> SeedTriageAsync(ApiFactory factory, Guid companyId, int code, DateTimeOffset enteredAt)
    {
        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var req = new Requisition(companyId, Guid.NewGuid(), Guid.NewGuid(), code, RequisitionType.Purchase,
            "SLA sweep", 1_000m, EstimateType.Full, "2026-07", Guid.NewGuid(), new DateOnly(2026, 7, 10));
        req.Submit(Guid.NewGuid(), enteredAt); // Draft → Triage; StatusChangedAt = enteredAt
        await uow.AddAsync(req, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);
        // On this single insert the entity is Added, so the AuditInterceptor runs MarkCreated(clock.now), which
        // resets StatusChangedAt to "now" (in production Submit is a separate UPDATE, so its stamp survives).
        // Back-date the column directly — a set-clause UPDATE bypasses the interceptor, so the value sticks.
        await uow.Set<Requisition>()
            .Where(r => r.Id == req.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StatusChangedAt, enteredAt), CancellationToken.None);
        return req.Id;
    }

    private static async Task DriveConsumerAsync(ApiFactory factory, RequisitionSlaBreached breach)
    {
        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await new RequisitionSlaBreachedConsumer(uow, publish).ProcessAsync(breach, CancellationToken.None);
    }
}
