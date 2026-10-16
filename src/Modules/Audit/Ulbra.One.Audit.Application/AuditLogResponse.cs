using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Audit.Domain;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Audit.Application;

[SwaggerSchema("One audited change, captured at the persistence layer rather than by each feature. That "
    + "is why it speaks in tables and columns instead of domain terms — it records what was written, not "
    + "what the user meant to do.")]
public sealed record AuditLogResponse(
    AuditLogId Id,
    [property: SwaggerSchema("When the change was committed.")]
    DateTimeOffset OccurredAt,
    [property: SwaggerSchema("Trace id of the request that made the change — the same value errors return "
        + "as `trace_id`, so a reported failure can be tied to what it actually wrote.")]
    [property: SwaggerExample("4bf92f3577b34da6a3ce929d0e0e4736")]
    string? TraceId,
    [property: SwaggerSchema("Who made the change.")]
    string ActorUserId,
    [property: SwaggerSchema("Whether the row was inserted, updated or deleted.")]
    AuditAction Action,
    [property: SwaggerSchema("CLR type of the entity behind the row.")]
    [property: SwaggerExample("Payable")]
    string EntityType,
    [property: SwaggerSchema("Database schema — effectively which module owns the data.")]
    [property: SwaggerExample("finance")]
    string Schema,
    [property: SwaggerSchema("Table the row lives in.")]
    [property: SwaggerExample("payables")]
    string TableName,
    [property: SwaggerSchema("Primary key of the affected row, as text.")]
    string PrimaryKey,
    [property: SwaggerSchema("The previous values, as a JSON object. Null on an insert, since there was "
        + "nothing before.")]
    string? OldValues,
    [property: SwaggerSchema("The new values, as a JSON object. Null on a delete.")]
    string? NewValues,
    [property: SwaggerSchema("Which columns changed, as a JSON array. It is what makes an update "
        + "readable without diffing the two objects by hand.")]
    string? AffectedColumns)
{
    public static AuditLogResponse From(AuditLog a) => new(new AuditLogId(a.Id), a.OccurredAt, a.TraceId, a.ActorUserId,
        a.Action, a.EntityType, a.Schema, a.TableName, a.PrimaryKey, a.OldValues, a.NewValues, a.AffectedColumns);
}
