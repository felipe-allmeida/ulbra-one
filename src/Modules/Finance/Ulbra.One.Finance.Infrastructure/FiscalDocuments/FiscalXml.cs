using System.Globalization;
using System.Linq;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>Shared parsing helpers for fiscal XML values (invariant culture: dot decimals, ISO dates).</summary>
internal static class FiscalXml
{
    public static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return DateOnly.FromDateTime(dto.DateTime);
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    public static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Derives the LC 116 subitem in the canonical "N.NN" form from the NFS-e national-standard <c>cTribNac</c>
    /// (Código de Tributação Nacional). The code is 6 numeric digits — item (2) + subitem (2) + national breakdown
    /// (2); the first four are the LC 116 item.subitem, and the trailing breakdown has no LC 116 counterpart, so it
    /// is dropped. The item drops any leading zero to match the official LC 116 numbering (items 1–40, unpadded)
    /// and the subitem keys the withholding rules/maps use ("010701" → "1.07", "170100" → "17.01"). Returns null
    /// for a malformed code (not exactly 6 digits) — best-effort: never fabricate a subitem the rules would match.
    /// </summary>
    public static string? Lc116FromCTribNac(string? cTribNac)
    {
        var value = cTribNac?.Trim();
        if (value is null || value.Length != 6 || !value.All(char.IsAsciiDigit)) return null;
        var item = int.Parse(value[..2], CultureInfo.InvariantCulture);
        return $"{item}.{value[2..4]}";
    }
}
