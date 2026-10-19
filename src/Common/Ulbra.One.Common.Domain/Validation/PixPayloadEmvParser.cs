using System.Globalization;
using System.Text;

namespace Ulbra.One.Common.Domain.Validation;

/// <summary>Point of Initiation of a PIX BR Code: reusable (static, key-based) or one-time (dynamic, URL-based).</summary>
public enum PixPayloadEmvInitiation { Static, Dynamic }

/// <summary>Outcome of parsing a PIX "copia e cola" (EMV/BR Code) payload.</summary>
public sealed record PixPayloadEmvParseResult(
    bool IsValid,
    bool CrcValid,
    PixPayloadEmvInitiation? Initiation,
    string? PixKey,
    string? PayloadLocationUrl,
    string? ReceiverName,
    string? ReceiverCity,
    decimal? Amount,
    string? Txid,
    string? Error);

/// <summary>
/// Parser for the PIX "copia e cola" payload (EMV®/BR Code, Bacen). Pure, stateless — no I/O. Distinguishes
/// a STATIC BR Code (tag 26 sub-tag 01 = a PIX key) from a DYNAMIC one (sub-tag 25 = a payload-location URL),
/// extracts the value the Santander CNAB240 remittance needs (Nota G041 — dynamic sends only the URL), and
/// validates the CRC16-CCITT (tag 63).
///
/// Deliberately defensive: it walks the TLV as far as it stays well-formed and reads the leading tags
/// (00/01/26) even when a later field is truncated — the Santander manual's own G041 examples redact the
/// trailing merchant fields, so a strict full-payload parse would reject them. Extraction never depends on
/// fields after tag 26.
/// </summary>
public static class PixPayloadEmvParser
{
    private const string PixGui = "br.gov.bcb.pix";
    private const string PayloadFormatPrefix = "000201"; // tag 00, len 02, value 01

    /// <summary>Cheap discriminator: "is this a BR Code at all?" — used to decide raw-key vs copia-e-cola
    /// without paying for a full parse on every ordinary PIX key.</summary>
    public static bool LooksLikeBrCode(string? s)
    {
        var t = s?.Trim();
        return t is { Length: >= 20 } && t.StartsWith(PayloadFormatPrefix, StringComparison.Ordinal);
    }

    public static PixPayloadEmvParseResult Parse(string? payload)
    {
        var s = payload?.Trim() ?? string.Empty;
        if (!LooksLikeBrCode(s))
            return Invalid("not an EMV BR Code");

        var top = WalkTlv(s);

        string? poi = Value(top, "01");
        var (key, url) = ReadPixTemplate(top);

        if (key is null && url is null)
            return Invalid("no PIX merchant account (tag 26)");
        if (key is not null && url is not null)
            return Invalid("ambiguous: both static key and dynamic URL present");

        var initiation = poi switch
        {
            "11" => PixPayloadEmvInitiation.Static,
            "12" => PixPayloadEmvInitiation.Dynamic,
            _ => url is not null ? PixPayloadEmvInitiation.Dynamic : PixPayloadEmvInitiation.Static,
        };

        decimal? amount = null;
        var amountRaw = Value(top, "54");
        if (amountRaw is not null && decimal.TryParse(amountRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var a))
            amount = a;

        var additional = Value(top, "62");
        var txid = additional is null ? null : Value(WalkTlv(additional), "05");

        return new PixPayloadEmvParseResult(
            IsValid: true,
            CrcValid: IsCrcValid(s),
            Initiation: initiation,
            PixKey: key,
            PayloadLocationUrl: url,
            ReceiverName: Value(top, "59"),
            ReceiverCity: Value(top, "60"),
            Amount: amount,
            Txid: txid,
            Error: null);
    }

    /// <summary>Walks a TLV string into (id, value) pairs, stopping at the first malformed object instead of
    /// throwing — so the leading well-formed tags are still available when a later field is truncated.</summary>
    private static List<(string Id, string Value)> WalkTlv(string s)
    {
        var result = new List<(string, string)>();
        var i = 0;
        while (i + 4 <= s.Length)
        {
            var id = s.Substring(i, 2);
            var lenText = s.Substring(i + 2, 2);
            if (!int.TryParse(lenText, NumberStyles.None, CultureInfo.InvariantCulture, out var len))
                break;
            var start = i + 4;
            if (start + len > s.Length)
                break;
            result.Add((id, s.Substring(start, len)));
            i = start + len;
        }
        return result;
    }

    /// <summary>Finds the first Merchant Account Information template (ids 26..51) whose GUI (sub-tag 00) is
    /// the PIX GUID and returns its key (sub-tag 01) and/or payload URL (sub-tag 25, scheme-stripped).</summary>
    private static (string? Key, string? Url) ReadPixTemplate(List<(string Id, string Value)> top)
    {
        foreach (var (id, value) in top)
        {
            if (string.Compare(id, "26", StringComparison.Ordinal) < 0 || string.Compare(id, "51", StringComparison.Ordinal) > 0)
                continue;

            var subs = WalkTlv(value);
            var gui = Value(subs, "00");
            if (gui is null || !gui.Trim().Equals(PixGui, StringComparison.OrdinalIgnoreCase))
                continue;

            var key = Value(subs, "01");
            var url = Value(subs, "25");
            return (NullIfEmpty(key), StripScheme(NullIfEmpty(url)));
        }
        return (null, null);
    }

    /// <summary>CRC16-CCITT/FALSE (poly 0x1021, init 0xFFFF) over everything up to and including the "6304"
    /// tag+len, compared case-insensitively to the trailing 4 hex chars. Independent of the TLV walk so a
    /// redacted-but-CRC-shaped payload reports CrcValid=false rather than throwing.</summary>
    private static bool IsCrcValid(string s)
    {
        if (s.Length < 8 || !s.AsSpan(s.Length - 8, 4).SequenceEqual("6304"))
            return false;
        var input = s[..^4];
        var expected = s[^4..];
        return Crc16(input).ToString("X4", CultureInfo.InvariantCulture)
            .Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static int Crc16(string input)
    {
        // Over the UTF-8 BYTE stream — a BR Code is a byte string (a recebedor name may carry accents), so a
        // per-char (& 0xFF) CRC would diverge on any non-ASCII byte and wrongly reject a legitimate payload.
        var crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(input))
        {
            crc ^= b << 8;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
        }
        return crc;
    }

    private static string? Value(List<(string Id, string Value)> tlv, string id)
    {
        foreach (var (i, v) in tlv)
            if (i == id)
                return v;
        return null;
    }

    private static string? StripScheme(string? url)
    {
        if (url is null)
            return null;
        foreach (var scheme in (ReadOnlySpan<string>)["https://", "http://"])
            if (url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                return url[scheme.Length..];
        return url;
    }

    private static string? NullIfEmpty(string? v) => string.IsNullOrEmpty(v) ? null : v;

    private static PixPayloadEmvParseResult Invalid(string error) =>
        new(false, false, null, null, null, null, null, null, null, error);
}
