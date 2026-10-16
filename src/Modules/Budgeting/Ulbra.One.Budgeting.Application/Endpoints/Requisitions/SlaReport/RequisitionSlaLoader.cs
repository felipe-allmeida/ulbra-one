using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Reports;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.SlaReport;

/// <summary>Loads the two flat feeds the SLA calculator consumes: the live head scalars (requisitions
/// currently in an SLA stage) and the historical transition trail. Optionally scoped to a company
/// (omitted = consolidated) and/or unit. The window is applied to the candidate scan only — a candidate's
/// full chain is loaded so exits and the Submit anchor outside the window are available.</summary>
internal static class RequisitionSlaLoader
{
    public static async Task<List<OpenStage>> LoadOpenStagesAsync(
        IUnitOfWork uow, Guid? companyId, Guid? unitId, CancellationToken ct)
    {
        var query = uow.ReadSet<Requisition>()
            .Where(r => r.Status == RequisitionStatus.Triage || r.Status == RequisitionStatus.Approval);
        if (companyId is { } cid) query = query.Where(r => r.CompanyId == cid);
        if (unitId is { } uid) query = query.Where(r => r.UnitId == uid);

        return await query
            .Select(r => new OpenStage(
                r.Id, r.Code, r.CompanyId, r.UnitId, r.Description, r.Status, r.StatusChangedAt, r.EstimateValue))
            .ToListAsync(ct);
    }

    public static async Task<List<RequisitionStageTransition>> LoadHistoryAsync(
        IUnitOfWork uow, Guid? companyId, Guid? unitId,
        DateTimeOffset? fromInstant, DateTimeOffset? toExclusive, CancellationToken ct)
    {
        var transitions = uow.ReadSet<RequisitionTransition>();
        var requisitions = uow.ReadSet<Requisition>();

        // Candidate requisitions: a stage entry (Triage/Approval) or the Approve (→ InPayment, closes the cycle)
        // landing in the window. Served by the (to_status, occurred_at) index (AEL-872).
        var candidateIds =
            (from t in transitions
             join r in requisitions on t.RequisitionId equals r.Id
             where (t.ToStatus == RequisitionStatus.Triage
                    || t.ToStatus == RequisitionStatus.Approval
                    || t.ToStatus == RequisitionStatus.InPayment)
                && (companyId == null || r.CompanyId == companyId)
                && (unitId == null || r.UnitId == unitId)
                && (fromInstant == null || t.OccurredAt >= fromInstant)
                && (toExclusive == null || t.OccurredAt < toExclusive)
             select t.RequisitionId)
            .Distinct();

        var query =
            from t in transitions
            join r in requisitions on t.RequisitionId equals r.Id
            where candidateIds.Contains(t.RequisitionId)
            select new RequisitionStageTransition(
                t.RequisitionId, r.CompanyId, r.UnitId, t.FromStatus, t.ToStatus, t.Action, t.OccurredAt);

        return await query.ToListAsync(ct);
    }
}
