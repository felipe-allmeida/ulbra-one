using Ulbra.One.Audit.Domain;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Audit.Application;

public sealed record AuditLogResponse(AuditLogId Id, DateTimeOffset OccurredAt, string? TraceId, string ActorUserId,
    AuditAction Action, string EntityType, string Schema, string TableName, string PrimaryKey,
    string? OldValues, string? NewValues, string? AffectedColumns)
{
    public static AuditLogResponse From(AuditLog a) => new(new AuditLogId(a.Id), a.OccurredAt, a.TraceId, a.ActorUserId,
        a.Action, a.EntityType, a.Schema, a.TableName, a.PrimaryKey, a.OldValues, a.NewValues, a.AffectedColumns);
}
