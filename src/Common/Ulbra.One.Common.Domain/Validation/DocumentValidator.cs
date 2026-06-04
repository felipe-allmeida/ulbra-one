using System.Linq;

namespace Ulbra.One.Common.Domain.Validation;

/// <summary>CPF/CNPJ validation with check-digit (módulo 11) verification.</summary>
public static class DocumentValidator
{
    public static string Normalize(string? document) =>
        new string((document ?? string.Empty).Where(char.IsDigit).ToArray());

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
        var cnpj = Normalize(document);
        if (cnpj.Length != 14 || cnpj.Distinct().Count() == 1) return false;

        var digits = cnpj.Select(c => c - '0').ToArray();
        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var d1 = CheckDigitWeighted(digits, w1);
        var d2 = CheckDigitWeighted(digits, w2);
        return digits[12] == d1 && digits[13] == d2;
    }

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
