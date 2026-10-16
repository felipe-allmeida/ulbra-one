using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Application.Endpoints.GetUnreadCount;

/// <summary>Unread count for the caller's audience — feeds the bell badge (polled by the frontend).</summary>
public sealed class GetUnreadNotificationsCount : IEndpoint
{
    [SwaggerSchema("How many unread notifications the caller has.")]
    public sealed record Response(
        [property: SwaggerSchema("Unread count for the current user — what the inbox badge shows.")]
        [property: SwaggerExample("7")]
        int Count);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/unread-count", Handle).WithName("GetUnreadNotificationsCount").WithTags("Notifications")
            .WithSummary("Counts the caller's unread notifications.")
            .WithDescription("How many unread notifications are in the caller's audience — the number "
                + "behind the bell badge, which the frontend polls. Same audience rule as the inbox "
                + "listing, so the count always matches what a request with unreadOnly=true would return.")
            .RequireAuthorization().Produces<Response>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(UserContext userContext, IUnitOfWork uow, CancellationToken ct)
    {
        var userId = Guid.Parse(userContext.UserId);
        var count = await uow.ReadSet<Notification>()
            .Where(NotificationAudience.For(userId, userContext.Role, userContext.IsAdmin))
            .Where(n => n.ReadAt == null)
            .CountAsync(ct);
        return TypedResults.Ok(new Response(count));
    }
}
