using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Notifications.Application.Endpoints.GetUnreadCount;
using Ulbra.One.Notifications.Application.Endpoints.ListNotifications;
using Ulbra.One.Notifications.Application.Endpoints.MarkAllNotificationsRead;
using Ulbra.One.Notifications.Application.Endpoints.MarkNotificationRead;

namespace Ulbra.One.Notifications.Application;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder builder)
    {
        var notifications = builder.MapGroup("/notifications");
        notifications.MapEndpoint<ListNotifications>();
        notifications.MapEndpoint<GetUnreadNotificationsCount>();
        notifications.MapEndpoint<MarkNotificationRead>();
        notifications.MapEndpoint<MarkAllNotificationsRead>();
        return builder;
    }
}
