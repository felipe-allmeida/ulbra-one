namespace Ulbra.One.Common.Application.Auditing;

/// <summary>
/// Reads change timestamps from the audit log without coupling callers to the Audit module's domain.
/// Implemented in Audit.Infrastructure; consumed by modules that need precise "when did X change" times
/// for actions their own entities don't timestamp.
/// </summary>
public interface IAuditReader
{
    /// <summary>Latest time an Update to (entityType, primaryKey) touched <paramref name="affectedColumn"/>, or null.</summary>
    Task<DateTimeOffset?> GetLatestChangeAsync(string entityType, string primaryKey, string affectedColumn, CancellationToken ct);

    /// <summary>For each primary key, the latest time an Update touched <paramref name="affectedColumn"/> (keys with no match are omitted).</summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> GetLatestChangesAsync(
        string entityType, IReadOnlyCollection<string> primaryKeys, string affectedColumn, CancellationToken ct);

    /// <summary>Every Update to (entityType, primaryKey), oldest first — the durable change history used to
    /// rebuild a lifecycle timeline whose events later clear the underlying fields (e.g. a returned-then-reopened
    /// payable, where Reopen clears RejectedAt/RejectionReason).</summary>
    Task<IReadOnlyList<AuditChangeRecord>> GetUpdateHistoryAsync(string entityType, string primaryKey, CancellationToken ct);
}
