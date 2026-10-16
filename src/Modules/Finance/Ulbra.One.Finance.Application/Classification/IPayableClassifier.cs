using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Classification;

public sealed record PayableClassificationResult(
    Guid? AccountingAccountId, double Confidence, string? Rationale, string Source);

/// <summary>Stable values for <see cref="PayableClassificationResult.Source"/>.</summary>
public static class ClassificationSources
{
    public const string Ai = "ai";
    public const string Heuristic = "heuristic";
    public const string None = "none";
    /// <summary>The AI provider was unavailable (down, rate-limited/429, or not configured). Distinct from
    /// <see cref="None"/> so the consumer settles for a human instead of redelivering into the throttle.</summary>
    public const string Unavailable = "unavailable";
}

public interface IPayableClassifier
{
    Task<PayableClassificationResult> ClassifyAsync(Payable payable, CancellationToken ct);
}
