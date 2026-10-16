namespace Ulbra.One.IntegrationTests;

/// <summary>Generates unique, valid (mod-11) numeric CNPJs for integration tests. Vendors are globally
/// unique by document, and the suite shares one database, so a fixed CNPJ would collide across tests.</summary>
public static class TestCnpj
{
    // Start high to avoid clashing with the dev seed's real CNPJs; incremented per call for uniqueness.
    private static int _seq = 90_000_000;

    public static string New()
    {
        var baseDigits = Interlocked.Increment(ref _seq).ToString("D8") + "0001"; // 12 digits (root + branch 0001)
        var dv1 = CheckDigit(baseDigits, 12);
        var dv2 = CheckDigit(baseDigits + dv1, 13);
        return baseDigits + dv1 + dv2;
    }

    /// <summary>Formats 14 digits as "00.000.000/0000-00" (to exercise the search normalization).</summary>
    public static string Format(string digits) =>
        $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..14]}";

    private static int CheckDigit(string digits, int len)
    {
        int[] weights = len == 12
            ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]
            : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var sum = 0;
        for (var i = 0; i < len; i++) sum += (digits[i] - '0') * weights[i];
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
