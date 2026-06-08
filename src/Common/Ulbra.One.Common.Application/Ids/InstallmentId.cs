using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Installment entities (wire form <c>installment_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<InstallmentId>))]
public readonly record struct InstallmentId(Guid Value) : IPrefixedId, IParsable<InstallmentId>
{
    public static string Prefix => "installment_";

    public static InstallmentId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static InstallmentId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid InstallmentId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out InstallmentId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new InstallmentId(g);
        return true;
    }
}
