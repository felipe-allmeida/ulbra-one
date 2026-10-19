using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Infrastructure.Ai;

namespace Ulbra.One.UnitTests.Ai;

public class AiRegistrationTests
{
    private static IServiceProvider Build(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAi(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_provider_is_Fake()
    {
        var sp = Build(new Dictionary<string, string?>());

        Assert.IsType<FakeAiCompletion>(sp.GetRequiredService<IAiCompletion>());
    }

    [Fact]
    public void Anthropic_provider_resolves_MeaiAiCompletion()
    {
        var sp = Build(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "Anthropic",
            ["Ai:ApiKey"] = "test-key",
            ["Ai:Model"] = "claude-sonnet-4-6",
        });

        Assert.IsType<MeaiAiCompletion>(sp.GetRequiredService<IAiCompletion>());
    }

    [Fact]
    public void Anthropic_without_key_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "Anthropic",
        }));
    }
}
