using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Application.Endpoints.MarkNotificationRead;

/// <summary>Marks a single notification read (idempotent). 404 when it doesn't exist OR isn't in the caller's
/// audience — a caller can't read (or probe) another audience's notification.</summary>
public sealed class MarkNotificationRead : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/read", Handle).WithName("MarkNotificationRead").WithTags("Notifications")
            .WithSummary("Marks one notification as read.")
            .WithDescription("Idempotent — marking an already-read notification succeeds and changes "
                + "nothing. A notification outside the caller's audience returns 404 rather than 403, on "
                + "purpose: a 403 would confirm the id exists, letting a caller probe for other people's "
                + "notifications.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        NotificationId id, UserContext userContext, IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        var userId = Guid.Parse(userContext.UserId);
        var notification = await uow.Set<Notification>()
            .Where(NotificationAudience.For(userId, userContext.Role, userContext.IsAdmin))
            .FirstOrDefaultAsync(n => n.Id == id.Value, ct);
        if (notification is null) return TypedResults.NotFound();

        notification.MarkRead(clock.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok();
    }
}
