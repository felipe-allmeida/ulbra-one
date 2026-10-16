using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Infrastructure.Ai;

namespace Ulbra.One.UnitTests.Ai;

public sealed record Probe(string Value);

public class FakeAiCompletionTests
{
    [Fact]
    public async Task Fake_returns_disabled_failure_so_callers_degrade()
    {
        var sut = new FakeAiCompletion();

        var result = await sut.CompleteAsync<Probe>(new AiPrompt("sys", "user"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.Ai.Disabled, result.Error.Code);
    }
}
