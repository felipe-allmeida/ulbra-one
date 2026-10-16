using System.Linq;

namespace Ulbra.One.Common.Domain.Validation;

/// <summary>CPF/CNPJ mod-11 validation, including the alphanumeric CNPJ (RFB, Jul/2026).</summary>
public static class DocumentValidator
{
    /// <summary>Digits only — for CPF and legacy numeric documents.</summary>
    public static string Normalize(string? document) =>
        new string((document ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>CNPJ chars only (letters + digits), uppercased — unlike <see cref="Normalize"/> (digits only).</summary>
    public static string NormalizeCnpj(string? document) =>
        new string((document ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValidCpf(string? document)
    {
        var cpf = Normalize(document);
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1) return false;

        var digits = cpf.Select(c => c - '0').ToArray();
        var d1 = CheckDigit(digits, 9, 10);
        var d2 = CheckDigit(digits, 10, 11);
        return digits[9] == d1 && digits[10] == d2;
    }

    public static bool IsValidCnpj(string? document)
    {
        var cnpj = NormalizeCnpj(document);
        if (!IsCnpjShape(cnpj) || cnpj.Distinct().Count() == 1) return false;

        // Alphanumeric mod-11: value = (ASCII - '0'), so '0'..'9' -> 0..9 and 'A'..'Z' -> 17..42.
        var values = cnpj.Select(c => c - '0').ToArray();
        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var d1 = CheckDigitWeighted(values, w1);
        var d2 = CheckDigitWeighted(values, w2);
        return values[12] == d1 && values[13] == d2;
    }

    // First 12 positions alphanumeric (0-9 or A-Z); the 2 check digits are numeric.
    private static bool IsCnpjShape(string cnpj) =>
        cnpj.Length == 14
        && cnpj.Take(12).All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z'))
        && char.IsDigit(cnpj[12]) && char.IsDigit(cnpj[13]);

    // CPF: weights are descending from `startWeight` down to 2.
    private static int CheckDigit(int[] digits, int count, int startWeight)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
            sum += digits[i] * (startWeight - i);
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    private static int CheckDigitWeighted(int[] digits, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
            sum += digits[i] * weights[i];
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
