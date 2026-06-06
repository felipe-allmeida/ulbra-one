using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Customer entities (wire form <c>customer_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<CustomerId>))]
public readonly record struct CustomerId(Guid Value) : IPrefixedId, IParsable<CustomerId>
{
    public static string Prefix => "customer_";

    public static CustomerId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static CustomerId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid CustomerId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out CustomerId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s; // lenient: accept bare guid too
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new CustomerId(g);
        return true;
    }
}
