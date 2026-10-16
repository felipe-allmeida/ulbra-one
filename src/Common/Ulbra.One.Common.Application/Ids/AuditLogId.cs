namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for AuditLog entities (wire form <c>audit_log_{guid:N}</c>).</summary>
[PrefixedId("audit_log_")]
public readonly partial record struct AuditLogId(Guid Value);
