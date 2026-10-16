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

namespace Ulbra.One.Notifications.Application.Endpoints.MarkAllNotificationsRead;

/// <summary>Marks every unread notification in the caller's audience as read in one set-based update, and
/// returns how many were cleared. Uses ExecuteUpdate (no change-tracking) — safe because Notification is not
/// audited.</summary>
public sealed class MarkAllNotificationsRead : IEndpoint
{
    [SwaggerSchema("Outcome of marking the inbox read.")]
    public sealed record Response(
        [property: SwaggerSchema("How many notifications were marked read. Zero means there was nothing "
            + "unread — the call is idempotent, not failed.")]
        [property: SwaggerExample("7")]
        int Count);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/read-all", Handle).WithName("MarkAllNotificationsRead").WithTags("Notifications")
            .WithSummary("Marks every unread notification as read.")
            .WithDescription("Clears the caller's whole inbox in one set-based update and returns how many "
                + "notifications were cleared. Scoped by the same audience rule as the listing, so it "
                + "never touches another user's notifications. Idempotent: calling it again returns 0.")
            .RequireAuthorization().Produces<Response>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        UserContext userContext, IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        var userId = Guid.Parse(userContext.UserId);
        var now = clock.GetUtcNow();
        var count = await uow.Set<Notification>()
            .Where(NotificationAudience.For(userId, userContext.Role, userContext.IsAdmin))
            .Where(n => n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return TypedResults.Ok(new Response(count));
    }
}
