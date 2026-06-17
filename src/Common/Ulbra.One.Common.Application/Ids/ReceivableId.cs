using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Receivable entities (wire form <c>receivable_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<ReceivableId>))]
public readonly record struct ReceivableId(Guid Value) : IPrefixedId, IParsable<ReceivableId>
{
    public static string Prefix => "receivable_";

    public static ReceivableId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static ReceivableId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid ReceivableId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out ReceivableId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new ReceivableId(g);
        return true;
    }
}
