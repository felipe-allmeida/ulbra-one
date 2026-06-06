using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Unit entities (wire form <c>unit_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<UnitId>))]
public readonly record struct UnitId(Guid Value) : IPrefixedId, IParsable<UnitId>
{
    public static string Prefix => "unit_";

    public static UnitId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static UnitId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid UnitId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out UnitId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s; // lenient: accept bare guid too
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new UnitId(g);
        return true;
    }
}
