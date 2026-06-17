using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Notifications.Application.Consumers;

namespace Ulbra.One.Persistence;

public static class MessagingModule
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(x =>
        {
            x.AddConsumer<PayablePendingApprovalConsumer>();

            x.AddEntityFrameworkOutbox<AppDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                var mt = configuration.GetSection("MassTransit");
                cfg.Host(mt["Host"] ?? "localhost", h =>
                {
                    h.Username(mt["User"] ?? "guest");
                    h.Password(mt["Password"] ?? "guest");
                });
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
