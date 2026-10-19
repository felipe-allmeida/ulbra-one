using System.Globalization;

namespace Ulbra.One.Finance.Infrastructure.Conciliation;

/// <summary>
/// Parses an OFX monetary value (<c>TRNAMT</c>/<c>BALAMT</c>) into a <see cref="decimal"/>, tolerating both
/// the invariant form (<c>-1000.00</c>) and the pt-BR form Brazilian banks export (<c>-200,00</c>,
/// <c>2650.000,00</c>). The library previously used caused a <c>FormatException</c> on <c>2650.000,00</c>
/// and silently multiplied <c>-200,00</c> by 100.
/// </summary>
public static class OfxAmount
{
    /// <summary>
    /// The right-most <c>.</c>/<c>,</c> followed by 1–2 digits is the decimal separator; every other
    /// <c>.</c>/<c>,</c> is a grouping separator and is stripped. If the right-most candidate is followed
    /// by 0 or ≥3 digits, all separators are treated as grouping (an integer amount).
    /// </summary>
    public static decimal Parse(string? raw)
    {
        var s = raw?.Trim() ?? string.Empty;
        if (s.Length == 0)
            return 0m;

        var lastSep = Math.Max(s.LastIndexOf('.'), s.LastIndexOf(','));
        string normalized;

        if (lastSep < 0)
        {
            normalized = s;
        }
        else
        {
            var charsAfter = s.Length - lastSep - 1;
            if (charsAfter is 1 or 2)
            {
                var intPart = s[..lastSep].Replace(".", "").Replace(",", "");
                var fracPart = s[(lastSep + 1)..];
                normalized = intPart + "." + fracPart;
            }
            else
            {
                normalized = s.Replace(".", "").Replace(",", "");
            }
        }

        return decimal.Parse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
    }
}
