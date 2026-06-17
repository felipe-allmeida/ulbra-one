using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Notifications.Application.Endpoints.ListNotifications;

namespace Ulbra.One.Notifications.Application;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder builder)
    {
        var notifications = builder.MapGroup("/notifications");
        notifications.MapEndpoint<ListNotifications>();
        return builder;
    }
}
