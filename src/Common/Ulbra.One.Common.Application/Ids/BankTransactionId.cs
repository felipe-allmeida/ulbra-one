using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for BankTransaction entities (wire form <c>bank_txn_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<BankTransactionId>))]
public readonly record struct BankTransactionId(Guid Value) : IPrefixedId, IParsable<BankTransactionId>
{
    public static string Prefix => "bank_txn_";

    public static BankTransactionId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static BankTransactionId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid BankTransactionId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out BankTransactionId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new BankTransactionId(g);
        return true;
    }
}
