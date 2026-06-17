using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Notifications.Application.Consumers;
using Ulbra.One.Persistence;

namespace Ulbra.One.Api;

internal static class MessagingSetup
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
                // Short query delay keeps notifications prompt in production and makes
                // integration tests deterministic (default is ~10 s which is too slow).
                o.QueryDelay = TimeSpan.FromSeconds(1);
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                var mt = configuration.GetSection("MassTransit");
                var host = mt["Host"] ?? "localhost";
                var portStr = mt["Port"];
                var port = ushort.TryParse(portStr, out var p) ? p : (ushort)5672;
                cfg.Host(host, port, "/", h =>
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
