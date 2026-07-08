using System.Globalization;
using System.Linq;
using Ulbra.One.Common.Domain.Validation;

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
    /// Derives the canonical LC 116 subitem from the NFS-e national-standard <c>cTribNac</c> (Código de Tributação
    /// Nacional): 6 numeric digits = item (2) + subitem (2) + desdobro nacional (2). Only the first four (item +
    /// subitem) map to LC 116; canonicalization (dropping the item's leading zero — "010701" → "1.07") is delegated
    /// to <see cref="Lc116.Canonical"/>, the single choke-point shared with the ABRASF parser and the rule editor.
    /// Returns null for a malformed code (not exactly 6 digits).
    /// </summary>
    public static string? Lc116FromCTribNac(string? cTribNac)
    {
        var value = cTribNac?.Trim();
        if (value is null || value.Length != 6 || !value.All(char.IsAsciiDigit)) return null;
        return Lc116.Canonical(value[..4]);
    }
}
