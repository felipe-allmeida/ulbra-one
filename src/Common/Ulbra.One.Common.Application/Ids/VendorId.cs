using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Vendor entities (wire form <c>vendor_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<VendorId>))]
public readonly record struct VendorId(Guid Value) : IPrefixedId, IParsable<VendorId>
{
    public static string Prefix => "vendor_";

    public static VendorId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static VendorId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid VendorId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out VendorId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s; // lenient: accept bare guid too
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new VendorId(g);
        return true;
    }
}
