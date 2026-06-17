using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Notifications.Infrastructure;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // No module-specific services in v1; consumer is registered via MassTransit in MessagingModule.
        return services;
    }
}
