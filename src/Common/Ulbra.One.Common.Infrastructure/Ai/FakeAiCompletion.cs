using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Common.Infrastructure.Ai;

/// <summary>Offline provider: AI is not configured. Returns a Disabled failure so consumers fall back.</summary>
public sealed class FakeAiCompletion : IAiCompletion
{
    public Task<Result<TResult>> CompleteAsync<TResult>(
        AiPrompt prompt, AiOptions? options = null, CancellationToken ct = default)
        where TResult : class
        => Task.FromResult(Result.Failure<TResult>(
            Error.Failure(ErrorCodes.Ai.Disabled, "AI provider is not configured.")));
}
