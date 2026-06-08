using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Payable entities (wire form <c>payable_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<PayableId>))]
public readonly record struct PayableId(Guid Value) : IPrefixedId, IParsable<PayableId>
{
    public static string Prefix => "payable_";

    public static PayableId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static PayableId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid PayableId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out PayableId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new PayableId(g);
        return true;
    }
}
