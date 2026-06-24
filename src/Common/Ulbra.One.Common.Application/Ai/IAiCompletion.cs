using Ulbra.One.Common.Domain;

namespace Ulbra.One.Common.Application.Ai;

/// <summary>Provider-agnostic LLM completion that returns a strongly-typed, schema-validated result.</summary>
public interface IAiCompletion
{
    Task<Result<TResult>> CompleteAsync<TResult>(
        AiPrompt prompt,
        AiOptions? options = null,
        CancellationToken ct = default) where TResult : class;
}

/// <summary>A system + user prompt pair.</summary>
public sealed record AiPrompt(string System, string User);

/// <summary>Per-call options. <see cref="Effort"/> is reserved (not wired in v1).</summary>
public sealed record AiOptions(string? Model = null, AiEffort Effort = AiEffort.Medium);

public enum AiEffort { Low, Medium, High }

/// <summary>Stable error codes returned by <see cref="IAiCompletion"/>.</summary>
public static class AiErrors
{
    public const string Refused = "Ai.Refused";
    public const string SchemaMismatch = "Ai.SchemaMismatch";
    public const string ProviderError = "Ai.ProviderError";
    public const string Disabled = "Ai.Disabled";
}
