using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Common.Infrastructure.Ai;

/// <summary>Implements <see cref="IAiCompletion"/> over a Microsoft.Extensions.AI <see cref="IChatClient"/>.
/// The only type aware of M.E.AI. Schema is auto-generated from TResult by GetResponseAsync&lt;T&gt;.</summary>
public sealed class MeaiAiCompletion(IChatClient chatClient, ILogger<MeaiAiCompletion> logger) : IAiCompletion
{
    public async Task<Result<TResult>> CompleteAsync<TResult>(
        AiPrompt prompt, AiOptions? options = null, CancellationToken ct = default)
        where TResult : class
    {
#pragma warning disable MEAI001
        var userContents = new List<AIContent> { new TextContent(prompt.User) };
        if (prompt.Attachments is { Count: > 0 })
            foreach (var a in prompt.Attachments)
                userContents.Add(new DataContent(a.Data, a.MediaType));

        var messages = new[]
        {
            new ChatMessage(ChatRole.System, prompt.System),
            new ChatMessage(ChatRole.User, userContents),
        };
#pragma warning restore MEAI001

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
            // Previously swallowed silently. Log it (so the real provider error — e.g. a 429 or a PDF the
            // model rejects — is visible) and classify rate-limit/overload distinctly so the caller can stop
            // retrying instead of hammering a throttled provider.
            var rateLimited = IsRateLimited(ex);
            logger.LogWarning(ex, "AI completion failed ({Kind}) for {ResultType}",
                rateLimited ? "rate-limited/overloaded" : "provider-error", typeof(TResult).Name);
            return Result.Failure<TResult>(Error.Failure(
                rateLimited ? ErrorCodes.Ai.RateLimited : ErrorCodes.Ai.ProviderError, ex.Message));
        }

        if (response.FinishReason == ChatFinishReason.ContentFilter)
            return Result.Failure<TResult>(Error.Failure(ErrorCodes.Ai.Refused, "The model declined to answer."));

        if (response.TryGetResult(out var value) && value is not null)
            return Result.Success(value);

        return Result.Failure<TResult>(
            Error.Failure(ErrorCodes.Ai.SchemaMismatch, "The response did not match the expected schema."));
    }

    /// <summary>Provider-agnostic detection of a throttle/overload signal (HTTP 429 or Anthropic's 529
    /// "overloaded"), walking the inner-exception chain. String heuristics keep this independent of the
    /// concrete SDK exception type surfaced through <see cref="IChatClient"/>.</summary>
    private static bool IsRateLimited(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException http && http.StatusCode is HttpStatusCode.TooManyRequests)
                return true;

            var msg = e.Message;
            if (msg.Contains("429") || msg.Contains("529")
                || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("rate_limit", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("too many requests", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
