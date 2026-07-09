namespace Ulbra.One.Common.Domain.Validation;

/// <summary>
/// Canonicalizes an LC 116 service subitem to the form "N.NN" (item without leading zero + 2-digit subitem) — the
/// single form the withholding rules and the natureza/tipo-de-serviço maps key on. Accepts the variants that arrive
/// from different NFS-e standards and free-text entry: dotted ("1.07", "01.07", "17.01") or the 4-digit undotted
/// code ("0107"). Returns null for blank/malformed input (best-effort — never fabricate a subitem the rules would
/// match against).
/// <para>Note: the national <c>cTribNac</c> is 6 digits (item + subitem + desdobro nacional); callers must pass
/// only the item+subitem (the first 4 digits), not the full code.</para>
/// </summary>
public static class Lc116
{
    public static string? Canonical(string? raw)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return null;

        string itemDigits, subDigits;
        var dot = v.IndexOf('.');
        if (dot >= 0)
        {
            itemDigits = Digits(v[..dot]);
            subDigits = Digits(v[(dot + 1)..]);
        }
        else
        {
            var d = Digits(v);
            if (d.Length < 3) return null; // can't split into item + 2-digit subitem
            itemDigits = d[..^2];
            subDigits = d[^2..];
        }

        if (itemDigits.Length == 0 || subDigits.Length < 2 || !int.TryParse(itemDigits, out var item)) return null;
        return $"{item}.{subDigits[..2]}";
    }

    private static string Digits(string s) => new(s.Where(char.IsAsciiDigit).ToArray());
}
