namespace Ulbra.One.Audit.Domain.Entities;

/// <summary>Immutable change-log row. Append-only: never updated or deleted; not itself audited.</summary>
public sealed class AuditLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; private set; }
    public string? TraceId { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public AuditAction Action { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public string Schema { get; private set; } = string.Empty;
    public string TableName { get; private set; } = string.Empty;
    public string PrimaryKey { get; private set; } = string.Empty;
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public string? AffectedColumns { get; private set; }

    private AuditLog() { }

    public AuditLog(AuditAction action, string entityType, string schema, string tableName, string primaryKey,
        string? oldValues, string? newValues, string? affectedColumns, string actorUserId, string? traceId,
        DateTimeOffset occurredAt)
    {
        Action = action; EntityType = entityType; Schema = schema; TableName = tableName; PrimaryKey = primaryKey;
        OldValues = oldValues; NewValues = newValues; AffectedColumns = affectedColumns;
        ActorUserId = actorUserId; TraceId = traceId; OccurredAt = occurredAt;
    }
}
