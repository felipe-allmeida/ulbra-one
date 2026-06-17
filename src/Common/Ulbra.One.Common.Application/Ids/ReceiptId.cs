using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Receipt entities (wire form <c>receipt_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<ReceiptId>))]
public readonly record struct ReceiptId(Guid Value) : IPrefixedId, IParsable<ReceiptId>
{
    public static string Prefix => "receipt_";

    public static ReceiptId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static ReceiptId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid ReceiptId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out ReceiptId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new ReceiptId(g);
        return true;
    }
}
