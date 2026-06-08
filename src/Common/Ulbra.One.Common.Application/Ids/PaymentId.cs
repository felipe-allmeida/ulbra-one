using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Payment entities (wire form <c>payment_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<PaymentId>))]
public readonly record struct PaymentId(Guid Value) : IPrefixedId, IParsable<PaymentId>
{
    public static string Prefix => "payment_";

    public static PaymentId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static PaymentId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid PaymentId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out PaymentId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new PaymentId(g);
        return true;
    }
}
