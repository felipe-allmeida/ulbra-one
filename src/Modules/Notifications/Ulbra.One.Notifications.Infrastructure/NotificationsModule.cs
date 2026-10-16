using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Notifications.Infrastructure;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // No module-specific services: the consumers are registered via MassTransit in MessagingSetup
        // (API composition root) and the mapper is stateless. The SLA escalation is detected+published by
        // Budgeting's sweep (AEL-872) and consumed here via RequisitionSlaBreachedConsumer.
        services.AddModelConfiguration(typeof(NotificationsModule).Assembly);

        return services;
    }
}
