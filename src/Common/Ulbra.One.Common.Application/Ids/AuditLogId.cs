using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for AuditLog entities (wire form <c>audit_log_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<AuditLogId>))]
public readonly record struct AuditLogId(Guid Value) : IPrefixedId, IParsable<AuditLogId>
{
    public static string Prefix => "audit_log_";

    public static AuditLogId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static AuditLogId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid AuditLogId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out AuditLogId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new AuditLogId(g);
        return true;
    }
}
