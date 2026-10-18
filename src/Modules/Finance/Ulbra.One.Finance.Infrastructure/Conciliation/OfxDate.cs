using System.Globalization;

namespace Ulbra.One.Finance.Infrastructure.Conciliation;

/// <summary>
/// Parses an OFX date (<c>DTPOSTED</c>/<c>DTSTART</c>/<c>DTEND</c>) into a <see cref="DateOnly"/>.
/// OFX dates are <c>YYYYMMDD</c> optionally followed by a time and a bracketed timezone, e.g.
/// <c>20260612000000[-3:GMT]</c>. Only the leading <c>YYYYMMDD</c> is used (the posting day);
/// the all-zero sentinel <c>00000000</c> and malformed values return <c>null</c>.
/// </summary>
public static class OfxDate
{
    public static DateOnly? ParseDate(string? raw)
    {
        var s = raw?.Trim() ?? string.Empty;
        if (s.Length < 8)
            return null;

        var head = s[..8];
        if (!head.All(char.IsDigit))
            return null;

        var year = int.Parse(head[..4], CultureInfo.InvariantCulture);
        var month = int.Parse(head.Substring(4, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(head.Substring(6, 2), CultureInfo.InvariantCulture);

        if (year == 0 || month is < 1 or > 12 || day is < 1 or > 31)
            return null;

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
