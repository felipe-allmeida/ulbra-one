using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for BankAccount entities (wire form <c>bank_account_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<BankAccountId>))]
public readonly record struct BankAccountId(Guid Value) : IPrefixedId, IParsable<BankAccountId>
{
    public static string Prefix => "bank_account_";

    public static BankAccountId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static BankAccountId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid BankAccountId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out BankAccountId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new BankAccountId(g);
        return true;
    }
}
