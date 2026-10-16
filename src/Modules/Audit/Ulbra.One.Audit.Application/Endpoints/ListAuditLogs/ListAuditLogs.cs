using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Audit.Application.Endpoints.ListAuditLogs;

/// <summary>Query-string filters for the audit trail. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListAuditLogsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Entity type to filter by, matched exactly — the CLR type name of the audited entity, "
        + "not a route segment.")]
    [SwaggerExample("Payable")]
    public string? EntityType { get; set; }

    [SwaggerSchema("Primary key of the audited row, as recorded in the trail. Pair it with EntityType to "
        + "follow one entity's whole history.")]
    public string? EntityId { get; set; }

    [SwaggerSchema("Who made the change — the actor's user id as stamped at write time.")]
    public string? ActorUserId { get; set; }

    [SwaggerSchema("Keep entries from this instant onward, inclusive.")]
    [SwaggerExample("2026-08-01T00:00:00Z")]
    public DateTimeOffset? From { get; set; }

    [SwaggerSchema("Keep entries up to this instant, inclusive.")]
    [SwaggerExample("2026-08-31T23:59:59Z")]
    public DateTimeOffset? To { get; set; }
}

public sealed class ListAuditLogs : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListAuditLogs").WithTags("Audit")
            .WithSummary("Lists audit trail entries.")
            .WithDescription("The write trail across every module, newest first: what changed, on which "
                + "entity, by whom and when. Every filter is optional and combines with AND. The trail is "
                + "append-only and lives in its own store, so nothing here can be edited or deleted "
                + "through the API.")
            .RequirePermission(Permissions.AuditRead).Produces<PaginatedResponse<AuditLogResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [AsParameters] ListAuditLogsRequest req,
        [FromKeyedServices("audit")] IUnitOfWork uow, CancellationToken ct)
    {
        var q = uow.ReadSet<AuditLog>();
        if (!string.IsNullOrEmpty(req.EntityType)) { var entityType = req.EntityType; q = q.Where(x => x.EntityType == entityType); }
        if (!string.IsNullOrEmpty(req.EntityId)) { var entityId = req.EntityId; q = q.Where(x => x.PrimaryKey == entityId); }
        if (!string.IsNullOrEmpty(req.ActorUserId)) { var actorUserId = req.ActorUserId; q = q.Where(x => x.ActorUserId == actorUserId); }
        if (req.From is { } f) q = q.Where(x => x.OccurredAt >= f);
        if (req.To is { } t) q = q.Where(x => x.OccurredAt <= t);
        var page = await q.OrderByDescending(x => x.OccurredAt).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(AuditLogResponse.From));
    }
}
