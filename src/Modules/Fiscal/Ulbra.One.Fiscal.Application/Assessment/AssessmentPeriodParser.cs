using System.Globalization;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Application.Assessment;

/// <summary>Parses the <c>perApur</c> query string (YYYY-MM) into an <see cref="AssessmentPeriod"/>.
/// Shared by the assessment endpoints.</summary>
internal static class AssessmentPeriodParser
{
    public static bool TryParse(string? value, out AssessmentPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('-');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)) return false;
        if (year is < 2000 or > 9999 || month is < 1 or > 12) return false;
        period = new AssessmentPeriod(year, month);
        return true;
    }
}
