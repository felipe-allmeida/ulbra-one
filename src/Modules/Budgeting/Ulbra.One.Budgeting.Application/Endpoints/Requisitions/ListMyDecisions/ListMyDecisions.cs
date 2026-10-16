using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ListMyDecisions;

/// <summary>Query-string filters for the approver's decision history. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListMyDecisionsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Restrict to decisions on requisitions of one company. Omit to list across every "
        + "company — the endpoint is already scoped to the calling approver, so this is not a consolidated "
        + "view of anyone else's work.")]
    public CompanyId? CompanyId { get; set; }
}

/// <summary>Decisions (Approve / ReturnForComplement / Cancel) taken by the calling approver, newest
/// first — feeds the "Histórico" tab of the approval inbox (AEL-859). A requisition can appear more than
/// once (e.g. returned and later approved); each row is one decision, not one requisition.</summary>
public sealed class ListMyDecisions : IEndpoint
{
    private static readonly RequisitionAction[] DecisionActions =
        [RequisitionAction.Approve, RequisitionAction.ReturnForComplement, RequisitionAction.Cancel];

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/my-decisions", Handle)
            .WithName("ListMyRequisitionDecisions")
            .WithTags("Budgeting")
            .WithSummary("Lists the calling approver's decisions.")
            .WithDescription("Approve, return-for-complement and cancel decisions taken by the caller, "
                + "newest first — the \"Histórico\" tab of the approval inbox. Each row is one decision, not "
                + "one requisition, so a requisition that was returned and later approved appears twice. "
                + "The actor is always the authenticated caller; it cannot be queried for someone else.")
            .RequirePermission(Permissions.RequisitionsApprove)
            .Produces<PaginatedResponse<RequisitionDecisionResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [AsParameters] ListMyDecisionsRequest req,
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
            // Desempate por Id: OccurredAt sozinho não é único, e sem ordem estável o Skip/Take pagina
            // de forma não-determinística (uma linha some numa página e reaparece na adjacente).
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

        // Vendor names resolved in one batch (read-only cross-module lookup, same pattern as Finance).
        // IgnoreQueryFilters: uma decisão histórica sobre fornecedor depois soft-deletado ainda deve
        // mostrar o nome (senão a linha do histórico ficaria com o nome em branco).
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
