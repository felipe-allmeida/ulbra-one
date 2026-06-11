using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for ReceivableInstallment entities (wire form <c>receivable_installment_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<ReceivableInstallmentId>))]
public readonly record struct ReceivableInstallmentId(Guid Value) : IPrefixedId, IParsable<ReceivableInstallmentId>
{
    public static string Prefix => "receivable_installment_";

    public static ReceivableInstallmentId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static ReceivableInstallmentId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid ReceivableInstallmentId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out ReceivableInstallmentId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new ReceivableInstallmentId(g);
        return true;
    }
}
