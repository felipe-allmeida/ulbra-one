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
                    var anthropic = new AnthropicClient { ApiKey = aiOptions.ApiKey };
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
