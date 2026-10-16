namespace Ulbra.One.Common.Application.Auditing;

/// <summary>One persisted Update to an audited entity, from the append-only audit log. Durable history: it
/// survives later field clears (e.g. a payable's RejectedAt cleared on Reopen), so a lifecycle timeline can be
/// rebuilt from it. <paramref name="OldValues"/>/<paramref name="NewValues"/> are the raw JSON the interceptor
/// stored, keyed by CLR property name (enums serialized as their numeric value).</summary>
public sealed record AuditChangeRecord(
    DateTimeOffset OccurredAt,
    string ActorUserId,
    IReadOnlyList<string> AffectedColumns,
    string? OldValues,
    string? NewValues);
