using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for CostCenter entities (wire form <c>cost_center_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<CostCenterId>))]
public readonly record struct CostCenterId(Guid Value) : IPrefixedId, IParsable<CostCenterId>
{
    public static string Prefix => "cost_center_";

    public static CostCenterId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static CostCenterId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid CostCenterId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out CostCenterId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s; // lenient: accept bare guid too
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new CostCenterId(g);
        return true;
    }
}
