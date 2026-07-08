using System.Globalization;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Application.Assessment;

/// <summary>Parses the <c>perApur</c> query string (YYYY-MM) into an <see cref="AssessmentPeriod"/>.
/// Shared by the assessment endpoints. The accepted year range (2010–2099) matches the EFD-Reinf
/// <c>perApur</c> XSD pattern, so a period that parses here also validates in the export.</summary>
public static class AssessmentPeriodParser
{
    public static bool TryParse(string? value, out AssessmentPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('-');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)) return false;
        // 2010–2099: EFD-Reinf perApur XSD is 20[1-9][0-9]; wider years would pass here but fail the export.
        if (year is < 2010 or > 2099 || month is < 1 or > 12) return false;
        period = new AssessmentPeriod(year, month);
        return true;
    }
}
