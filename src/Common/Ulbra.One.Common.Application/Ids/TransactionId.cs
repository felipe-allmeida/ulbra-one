using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Transaction entities (wire form <c>transaction_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<TransactionId>))]
public readonly record struct TransactionId(Guid Value) : IPrefixedId, IParsable<TransactionId>
{
    public static string Prefix => "transaction_";

    public static TransactionId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static TransactionId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid TransactionId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out TransactionId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s; // lenient: accept bare guid too
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new TransactionId(g);
        return true;
    }
}
