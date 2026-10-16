using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Application.Sla;

public interface IRequisitionSlaSweepService
{
    Task<RequisitionSlaSweepResult> SweepAsync(DateTimeOffset now, CancellationToken ct);
}

public sealed record RequisitionSlaSweepResult(int CandidatesScanned, int Breached);

/// <summary>
/// Active SLA escalation (AEL-872) — the periodic sweep. Budgeting owns <see cref="Requisition"/>, so the
/// detection lives here: it queries the stages still open past their SLA and PUBLISHES one
/// <see cref="RequisitionSlaBreached"/> per breach (EF bus outbox, committed atomically with SaveChanges); the
/// Notifications module consumes it. No cross-module aggregate read — the boundary stays event-only.
/// <para>Idempotency: a deterministic <c>BreachId</c> over (requisition, stage, stage-entry). The sweep re-runs
/// (ACA cron Job) and re-publishes for still-overdue stages, but the consumer dedups on that key, so an
/// occurrence notifies at most once.</para>
/// Coarse SQL prefilter by calendar time (business-elapsed ≤ calendar-elapsed, so the SMALLER stage threshold is
/// a safe superset), then a precise per-stage business-hours check in memory.
/// </summary>
public sealed class RequisitionSlaSweepService(
    IUnitOfWork uow, IPublishEndpoint publish, IOptions<RequisitionSlaOptions> options,
    ILogger<RequisitionSlaSweepService> log) : IRequisitionSlaSweepService
{
    public async Task<RequisitionSlaSweepResult> SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        var sla = options.Value;
        var calendarCutoff = now - TimeSpan.FromHours(Math.Min(sla.TriageHours, sla.ApprovalHours));

        var candidates = await uow.ReadSet<Requisition>()
            .Where(r => (r.Status == RequisitionStatus.Triage || r.Status == RequisitionStatus.Approval)
                        && r.StatusChangedAt < calendarCutoff)
            .Select(r => new { r.Id, r.CompanyId, r.Code, r.Status, r.StatusChangedAt })
            .ToListAsync(ct);

        var breached = 0;
        foreach (var r in candidates)
        {
            var threshold = r.Status == RequisitionStatus.Triage ? sla.TriageHours : sla.ApprovalHours;
            if (BusinessTime.BusinessHoursBetween(r.StatusChangedAt, now) <= threshold) continue;
            breached++;

            var breachId = DeterministicGuid.From($"reqsla:{r.Id}:{r.Status}:{r.StatusChangedAt:O}");
            await publish.Publish(
                new RequisitionSlaBreached(breachId, r.Id, r.CompanyId, r.Code, r.Status.ToString(), threshold, now),
                ct);
        }

        // One save commits every enqueued outbox message atomically (nothing else is tracked — reads only).
        await uow.SaveChangesAsync(ct);
        log.LogInformation("Requisition SLA sweep: {Scanned} candidates, {Breached} overdue", candidates.Count, breached);
        return new RequisitionSlaSweepResult(candidates.Count, breached);
    }
}
