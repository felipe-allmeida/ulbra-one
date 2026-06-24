using Azure.Identity;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Persistence;

namespace Ulbra.One.Api;

internal static class MessagingSetup
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(x =>
        {
            x.AddConsumer<Ulbra.One.Finance.Infrastructure.Messaging.PayableReceivedConsumer>();

            x.AddEntityFrameworkOutbox<AppDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                // Short query delay keeps event delivery prompt in production and makes
                // integration tests deterministic (default is ~10 s which is too slow).
                o.QueryDelay = TimeSpan.FromSeconds(1);
            });

            var transport = configuration["MassTransit:Transport"] ?? "RabbitMq";

            if (transport.Equals("AzureServiceBus", StringComparison.OrdinalIgnoreCase))
            {
                x.UsingAzureServiceBus((context, cfg) =>
                {
                    var connectionString = configuration["MassTransit:ConnectionString"];
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        // Connection-string auth (e.g. local dev or non-MI environments)
                        cfg.Host(connectionString, _ => { });
                    }
                    else
                    {
                        // Passwordless via Managed Identity / DefaultAzureCredential
                        var ns = configuration["MassTransit:Namespace"]
                            ?? throw new InvalidOperationException(
                                "MassTransit:Namespace must be set when MassTransit:Transport=AzureServiceBus " +
                                "and MassTransit:ConnectionString is not provided.");
                        cfg.Host(new Uri($"sb://{ns}.servicebus.windows.net"), h =>
                        {
                            h.TokenCredential = new DefaultAzureCredential();
                        });
                    }

                    cfg.ConfigureEndpoints(context);
                });
            }
            else
            {
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
            }
        });

        return services;
    }
}
