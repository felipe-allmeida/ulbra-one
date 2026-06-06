using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Company entities (wire form <c>company_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<CompanyId>))]
public readonly record struct CompanyId(Guid Value) : IPrefixedId, IParsable<CompanyId>
{
    public static string Prefix => "company_";

    public static CompanyId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static CompanyId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid CompanyId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out CompanyId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s; // lenient: accept bare guid too
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new CompanyId(g);
        return true;
    }
}
