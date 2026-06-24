using Microsoft.Extensions.AI;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Common.Infrastructure.Ai;

/// <summary>Implements <see cref="IAiCompletion"/> over a Microsoft.Extensions.AI <see cref="IChatClient"/>.
/// The only type aware of M.E.AI. Schema is auto-generated from TResult by GetResponseAsync&lt;T&gt;.</summary>
public sealed class MeaiAiCompletion(IChatClient chatClient) : IAiCompletion
{
    public async Task<Result<TResult>> CompleteAsync<TResult>(
        AiPrompt prompt, AiOptions? options = null, CancellationToken ct = default)
        where TResult : class
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, prompt.System),
            new ChatMessage(ChatRole.User, prompt.User),
        };

        // Effort is reserved (not wired in v1). Model override flows to ChatOptions.ModelId.
        var chatOptions = options?.Model is { Length: > 0 } model
            ? new ChatOptions { ModelId = model }
            : null;

        ChatResponse<TResult> response;
        try
        {
            response = await chatClient.GetResponseAsync<TResult>(
                messages, chatOptions, useJsonSchemaResponseFormat: true, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result.Failure<TResult>(Error.Failure(AiErrors.ProviderError, ex.Message));
        }

        if (response.FinishReason == ChatFinishReason.ContentFilter)
            return Result.Failure<TResult>(Error.Failure(AiErrors.Refused, "The model declined to answer."));

        if (response.TryGetResult(out var value) && value is not null)
            return Result.Success(value);

        return Result.Failure<TResult>(
            Error.Failure(AiErrors.SchemaMismatch, "The response did not match the expected schema."));
    }
}
