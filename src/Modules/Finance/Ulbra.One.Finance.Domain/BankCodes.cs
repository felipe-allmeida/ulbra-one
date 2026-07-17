using System.Text;

namespace Ulbra.One.Finance.Domain;

/// <summary>Helpers for Brazilian COMPE bank codes (3-digit numeric).</summary>
public static class BankCodes
{
    /// <summary>Santander's COMPE code.</summary>
    public const string Santander = "033";

    /// <summary>
    /// Canonicalizes a COMPE bank code to its 3-digit zero-padded form:
    /// <c>"33"</c>/<c>"0033"</c>/<c>" 33 "</c>/<c>"033 - Santander"</c> → <c>"033"</c>.
    /// Empty, whitespace, all-zero or non-numeric input → <c>""</c>. Values with more than
    /// three significant digits are returned digits-only (not a valid COMPE code — they
    /// simply won't match a known bank).
    /// </summary>
    public static string Normalize(string? value)
    {
        var digits = Digits(value).TrimStart('0');
        if (digits.Length == 0)
            return "";
        return digits.Length <= 3 ? digits.PadLeft(3, '0') : digits;
    }

    /// <summary>True when <paramref name="value"/> canonicalizes to Santander's code, regardless of
    /// how it was entered ("33", "0033", "033", " 33 ").</summary>
    public static bool IsSantander(string? value) => Normalize(value) == Santander;

    private static string Digits(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsAsciiDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }
}
