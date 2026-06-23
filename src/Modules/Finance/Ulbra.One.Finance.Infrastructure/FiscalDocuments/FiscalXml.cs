using System.Globalization;

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
}
