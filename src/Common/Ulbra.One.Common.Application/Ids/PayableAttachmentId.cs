using System.Text.Json.Serialization;

namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for PayableAttachment entities (wire form <c>attachment_{guid:N}</c>).</summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<PayableAttachmentId>))]
public readonly record struct PayableAttachmentId(Guid Value) : IPrefixedId, IParsable<PayableAttachmentId>
{
    public static string Prefix => "attachment_";

    public static PayableAttachmentId New() => new(Guid.NewGuid());

    public override string ToString() => Prefix + Value.ToString("N");

    public static PayableAttachmentId Parse(string s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid PayableAttachmentId: {s}");

    public static bool TryParse(string? s, IFormatProvider? p, out PayableAttachmentId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var raw = s.StartsWith(Prefix, StringComparison.Ordinal) ? s[Prefix.Length..] : s;
        if (!Guid.TryParse(raw, out var g)) return false;
        result = new PayableAttachmentId(g);
        return true;
    }
}
