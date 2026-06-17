using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Audit.Application.Endpoints.ListAuditLogs;

public sealed class ListAuditLogs : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListAuditLogs").WithTags("Audit")
            .RequireAuthorization().Produces<PaginatedResponse<AuditLogResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        string? entityType, string? entityId, string? actorUserId, DateTimeOffset? from, DateTimeOffset? to,
        [AsParameters] PaginationQueryParameters pagination,
        [FromKeyedServices("audit")] IUnitOfWork uow, CancellationToken ct)
    {
        var q = uow.ReadSet<AuditLog>();
        if (!string.IsNullOrEmpty(entityType)) q = q.Where(x => x.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId)) q = q.Where(x => x.PrimaryKey == entityId);
        if (!string.IsNullOrEmpty(actorUserId)) q = q.Where(x => x.ActorUserId == actorUserId);
        if (from is { } f) q = q.Where(x => x.OccurredAt >= f);
        if (to is { } t) q = q.Where(x => x.OccurredAt <= t);
        var page = await q.OrderByDescending(x => x.OccurredAt).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(AuditLogResponse.From));
    }
}
