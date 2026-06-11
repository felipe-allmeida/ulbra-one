using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for ChartAccount entities (wire form <c>chart_account_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<ChartAccountId>))]
public readonly record struct ChartAccountId(Guid Value) : IPrefixedId, IParsable<ChartAccountId>
{
    public static string Prefix => "chart_account_";

    public static ChartAccountId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static ChartAccountId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid ChartAccountId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out ChartAccountId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new ChartAccountId(g);
        return true;
    }
}
