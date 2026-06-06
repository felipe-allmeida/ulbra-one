using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for User entities (wire form <c>user_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<UserId>))]
public readonly record struct UserId(Guid Value) : IPrefixedId, IParsable<UserId>
{
    public static string Prefix => "user_";

    public static UserId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static UserId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid UserId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out UserId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s; // lenient: accept bare guid too
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new UserId(g);
        return true;
    }
}
