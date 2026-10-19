using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ulbra.One.Common.Application.Ai;

namespace Ulbra.One.Common.Infrastructure.Ai;

/// <summary>Registers the config-selected AI provider (mirrors AddObjectStorage). Default Fake (offline).</summary>
public static class AiRegistration
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiProviderOptions.SectionName);
        services.Configure<AiProviderOptions>(section);
        var opts = section.Get<AiProviderOptions>() ?? new AiProviderOptions();

        switch (opts.Provider)
        {
            case AiProvider.Anthropic:
                if (string.IsNullOrWhiteSpace(opts.ApiKey))
                    throw new InvalidOperationException("Ai:ApiKey is required when Ai:Provider=Anthropic.");

                services.AddSingleton<IChatClient>(sp =>
                {
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    var aiOptions = sp.GetRequiredService<IOptions<AiProviderOptions>>().Value;
                    // MaxRetries = 0: the SDK's own retry (default 2 → ×3 HTTP per call on 429/5xx) would
                    // stack on top of the consumer's redelivery policy, multiplying calls to a throttled
                    // provider. Retry is owned intentionally by the consumer, so disable it here.
                    var anthropic = new AnthropicClient { ApiKey = aiOptions.ApiKey, MaxRetries = 0 };
                    return anthropic.AsIChatClient(aiOptions.Model)
                        .AsBuilder()
                        .UseOpenTelemetry(loggerFactory, AiTelemetry.SourceName)
                        .UseLogging(loggerFactory)
                        .Build();
                });
                services.AddScoped<IAiCompletion, MeaiAiCompletion>();
                break;

            case AiProvider.OpenAI:
            case AiProvider.AzureOpenAI:
                throw new InvalidOperationException(
                    $"Ai:Provider={opts.Provider} is reserved but not implemented yet.");

            default: // Fake
                services.AddScoped<IAiCompletion, FakeAiCompletion>();
                break;
        }

        return services;
    }
}
