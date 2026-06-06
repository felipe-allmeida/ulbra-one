using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Employee entities (wire form <c>employee_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<EmployeeId>))]
public readonly record struct EmployeeId(Guid Value) : IPrefixedId, IParsable<EmployeeId>
{
    public static string Prefix => "employee_";

    public static EmployeeId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static EmployeeId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid EmployeeId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out EmployeeId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s; // lenient: accept bare guid too
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new EmployeeId(g);
        return true;
    }
}
