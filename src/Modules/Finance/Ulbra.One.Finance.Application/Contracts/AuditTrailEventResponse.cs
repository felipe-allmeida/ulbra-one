namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One typed event on a title's audit timeline. <c>Type</c> matches the FE i18n keys under workflow.timeline.*.
/// <c>Metadata</c> carries small string extras (reason, amount, installment sequence) for display.
/// </summary>
public sealed record AuditTrailEventResponse(
    string Type,
    string? ActorId,
    string ActorName,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Metadata);
