using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ListMyTriageDecisions;

/// <summary>Query-string filters for the controller's triage history. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListMyTriageDecisionsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Restrict to decisions on requisitions of one company. Omit to list across every "
        + "company — the endpoint is already scoped to the calling controller.")]
    public CompanyId? CompanyId { get; set; }
}

/// <summary>Triage decisions (ApproveTriage / ReturnForComplement / Cancel) taken by the calling
/// controller, newest first — feeds the "Histórico" tab of the triage inbox. Triage counterpart of
/// ListMyDecisions (AEL-859); reuses <see cref="RequisitionDecisionResponse"/>. Scoped by ActorId, so
/// ReturnToTriageForRevalue (authored by the requester's complement) is naturally excluded.</summary>
public sealed class ListMyTriageDecisions : IEndpoint
{
    private static readonly RequisitionAction[] DecisionActions =
        [RequisitionAction.ApproveTriage, RequisitionAction.ReturnForComplement, RequisitionAction.Cancel];

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/my-triage-decisions", Handle)
            .WithName("ListMyRequisitionTriageDecisions")
            .WithTags("Budgeting")
            .WithSummary("Lists the calling controller's triage decisions.")
            .WithDescription("Approve-triage, return-for-complement and cancel decisions taken by the "
                + "caller, newest first — the \"Histórico\" tab of the triage inbox. Scoped by actor, so a "
                + "return-to-triage authored by the requester's own complement never shows up here.")
            .RequirePermission(Permissions.RequisitionsTriage)
            .Produces<PaginatedResponse<RequisitionDecisionResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [AsParameters] ListMyTriageDecisionsRequest req,
        UserContext userContext,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var actor = Guid.Parse(userContext.UserId);

        var query =
            from t in uow.ReadSet<RequisitionTransition>()
            where t.ActorId == actor && DecisionActions.Contains(t.Action)
            join r in uow.ReadSet<Requisition>() on t.RequisitionId equals r.Id
            select new { t, r };
        if (req.CompanyId is { } cid) query = query.Where(x => x.r.CompanyId == cid.Value);

        var page = await query
            // Id tiebreak: OccurredAt alone isn't unique (non-deterministic paging without a stable order).
            .OrderByDescending(x => x.t.OccurredAt).ThenByDescending(x => x.t.Id)
            .Select(x => new
            {
                x.r.Id,
                x.r.Code,
                x.r.VendorId,
                x.r.Description,
                x.r.EstimateValue,
                x.r.Status,
                x.t.Action,
                x.t.Reason,
                x.t.OccurredAt,
            })
            .ToPaginatedAsync(req, ct);

        // IgnoreQueryFilters: history about a later soft-deleted vendor still shows the name.
        var vendorIds = page.Items.Select(d => d.VendorId).Distinct().ToList();
        var vendorNames = await uow.ReadSet<VendorEntity>()
            .IgnoreQueryFilters()
            .Where(v => vendorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Name, ct);

        return TypedResults.Ok(page.Map(d => new RequisitionDecisionResponse(
            new RequisitionId(d.Id), d.Code, new VendorId(d.VendorId),
            vendorNames.GetValueOrDefault(d.VendorId, string.Empty),
            d.Description, d.EstimateValue, d.Status, d.Action, d.Reason, d.OccurredAt)));
    }
}
