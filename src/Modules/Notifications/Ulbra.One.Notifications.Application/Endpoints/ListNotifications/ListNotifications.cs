using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Application.Endpoints.ListNotifications;

public sealed class ListNotifications : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListNotifications").WithTags("Notifications")
            .RequireAuthorization().Produces<PaginatedResponse<NotificationResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [AsParameters] PaginationQueryParameters pagination,
        UserContext userContext,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var role = userContext.Role ?? string.Empty;
        var page = await uow.ReadSet<Notification>()
            .Where(n => n.AudienceRole == role)
            .OrderByDescending(n => n.OccurredAt)
            .ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(NotificationResponse.From));
    }
}
