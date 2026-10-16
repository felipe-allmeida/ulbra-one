namespace Ulbra.One.Common.Domain.Validation;

/// <summary>Canonical boleto linha digitável rules shared by Finance and Budgeting so the 47/48-digit check
/// lives in one place. <see cref="IsValid"/> is the blocking rule (length + convênio lead digit, not the DVs);
/// <see cref="CheckDigitsConsistent"/> is advisory (mod-10/mod-11) to flag an OCR misread — it never blocks.</summary>
public static class BoletoLine
{
    private const char ConvenioLeadDigit = '8';

    /// <summary>Digits only (drops mask separators/spaces) — the equivalent of the front-end's <c>replace(/\D/g,'')</c>.</summary>
    public static string Digits(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;
        var buffer = new char[line.Length];
        var count = 0;
        foreach (var ch in line)
        {
            if (char.IsAsciiDigit(ch))
                buffer[count++] = ch;
        }
        return new string(buffer, 0, count);
    }

    /// <summary>Arrecadação/convênio when the linha digitável begins with <c>8</c> (barcode product identifier).</summary>
    public static bool IsConvenio(string? line)
    {
        var digits = Digits(line);
        return digits.Length > 0 && digits[0] == ConvenioLeadDigit;
    }

    /// <summary>Blocking rule: 47 digits (bancário, lead not <c>8</c>) or 48 (convênio, lead <c>8</c>). Routing
    /// keys on the lead <c>8</c>, so a 48-without-8 or a 47-with-8 (a truncated convênio) is rejected before it
    /// reaches a length-sensitive barcode converter (which would throw → HTTP 500).</summary>
    public static bool IsValid(string? line)
    {
        var digits = Digits(line);
        return (digits.Length == 47 && digits[0] != ConvenioLeadDigit)
            || (digits.Length == 48 && digits[0] == ConvenioLeadDigit);
    }

    /// <summary>Advisory DV consistency (mod-10/mod-11) to flag an OCR misread on an AI-extracted line;
    /// never blocks. False when the line is not a well-formed 47/48.</summary>
    public static bool CheckDigitsConsistent(string? line)
    {
        var d = Digits(line);
        // Length disambiguates: a 47-digit line is always bancário (even if its bank code starts with 8);
        // only the 48-digit arrecadação/convênio carries the '8' product lead.
        if (d.Length == 47)
            return CheckBancario(d);
        if (d.Length == 48 && d[0] == ConvenioLeadDigit)
            return CheckConvenio(d);
        return false;
    }

    // Bancário 47: field DVs at indices 9, 20, 31 (mod-10 over the preceding significant digits) and the
    // general DV at index 32 (mod-11 over the 43-digit barcode, which excludes the general DV itself).
    private static bool CheckBancario(string d)
    {
        if (Mod10(d[..9]) != d[9] - '0') return false;
        if (Mod10(d.Substring(10, 10)) != d[20] - '0') return false;
        if (Mod10(d.Substring(21, 10)) != d[31] - '0') return false;

        // 43-digit barcode without the general DV: banco+moeda(0-3) + fator+valor(33-46) + the three
        // campos livres (4-8, 10-19, 21-30). Mirrors BoletoBarcode.BancarioTo44's reorder, minus the DV.
        var barcode43 = d[..4] + d.Substring(33, 14) + d.Substring(4, 5) + d.Substring(10, 10) + d.Substring(21, 10);
        return Mod11Bancario(barcode43) == d[32] - '0';
    }

    // Arrecadação 48: four blocks of 11 significant + 1 DV. The DV algorithm (mod-10 vs mod-11) is chosen by
    // the value identifier — the 3rd digit of the barcode, i.e. the 3rd digit of the line (blocks carry no
    // offset before it): 6/7 → mod-10, 8/9 → mod-11.
    private static bool CheckConvenio(string d)
    {
        var mod11 = d[2] is '8' or '9';
        for (var block = 0; block < 4; block++)
        {
            var start = block * 12;
            var significant = d.Substring(start, 11);
            var dv = d[start + 11] - '0';
            var expected = mod11 ? Mod11Arrecadacao(significant) : Mod10(significant);
            if (expected != dv)
                return false;
        }
        return true;
    }

    // FEBRABAN mod-10: right-to-left weights 2,1,2,1…; a product > 9 has its two digits summed.
    private static int Mod10(string significant)
    {
        var sum = 0;
        var weight = 2;
        for (var i = significant.Length - 1; i >= 0; i--)
        {
            var p = (significant[i] - '0') * weight;
            if (p > 9) p = (p / 10) + (p % 10);
            sum += p;
            weight = weight == 2 ? 1 : 2;
        }
        return (10 - (sum % 10)) % 10;
    }

    // Bancário general DV: mod-11 with right-to-left weights cycling 2..9. dv = 11 - remainder, so dv ∈ 1..11
    // (remainder 0 → 11, remainder 1 → 10); FEBRABAN maps a resulting 10 or 11 to DV 1.
    private static int Mod11Bancario(string significant)
    {
        var dv = 11 - Mod11Sum(significant) % 11;
        return dv is 10 or 11 ? 1 : dv;
    }

    // Arrecadação block DV: mod-11 with right-to-left weights cycling 2..9. Same dv = 11 - remainder, but the
    // FEBRABAN arrecadação convention maps a resulting 10 or 11 to DV 0 (i.e. remainder 0 or 1 → 0), not 1.
    private static int Mod11Arrecadacao(string significant)
    {
        var dv = 11 - Mod11Sum(significant) % 11;
        return dv is 10 or 11 ? 0 : dv;
    }

    private static int Mod11Sum(string significant)
    {
        var sum = 0;
        var weight = 2;
        for (var i = significant.Length - 1; i >= 0; i--)
        {
            sum += (significant[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }
        return sum;
    }
}
