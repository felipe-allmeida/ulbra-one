using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Application.Endpoints.ListNotifications;

/// <summary>The in-app inbox: the caller's notifications (personal + their role; Admin sees all role ones),
/// newest first, paginated. <c>?unreadOnly=true</c> restricts to unread. No dedicated permission — any
/// authenticated user has an inbox; the audience filter scopes what they see.</summary>
/// <summary>Query-string filters for the notification inbox. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListNotificationsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Return only unread notifications. Omitted or false returns read and unread alike.")]
    public bool? UnreadOnly { get; set; }
}

public sealed class ListNotifications : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListNotifications").WithTags("Notifications")
            .WithSummary("Lists the caller's notifications.")
            .WithDescription("The in-app inbox, newest first: notifications addressed to the caller "
                + "personally plus those addressed to their role, with Admin seeing every role-addressed "
                + "one. There is no permission gate — any authenticated user has an inbox, and the "
                + "audience filter is what scopes it, so the caller cannot ask for someone else's.")
            .RequireAuthorization().Produces<PaginatedResponse<NotificationResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [AsParameters] ListNotificationsRequest req,
        UserContext userContext,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var userId = Guid.Parse(userContext.UserId);
        var query = uow.ReadSet<Notification>().Where(NotificationAudience.For(userId, userContext.Role, userContext.IsAdmin));
        if (req.UnreadOnly == true) query = query.Where(n => n.ReadAt == null);

        var page = await query.OrderByDescending(n => n.OccurredAt).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(NotificationResponse.From));
    }
}
