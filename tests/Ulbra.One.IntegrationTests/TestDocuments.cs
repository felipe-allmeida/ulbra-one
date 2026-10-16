namespace Ulbra.One.IntegrationTests;

/// <summary>
/// Valid Brazilian documents for test fixtures.
/// <para>
/// The suite used to seed companies with <c>Random.Shared.NextInt64(...)</c> as a CNPJ — 14 random digits,
/// whose check digits almost never close. That went unnoticed because nothing validated the field: the API
/// accepted whatever arrived. The moment validation landed, 370 tests failed at their seed, which is a fair
/// measure of how much invalid data the fixtures were creating.
/// </para>
/// </summary>
internal static class TestDocuments
{
    /// <summary>A random CNPJ with correct check digits, unique enough for a fixture that seeds many companies.</summary>
    internal static string RandomCnpj()
    {
        var digits = new int[14];
        for (var i = 0; i < 12; i++) digits[i] = Random.Shared.Next(0, 10);
        digits[12] = CheckDigit(digits, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        digits[13] = CheckDigit(digits, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return string.Concat(digits);
    }

    // Mod-11, the same weights DocumentValidator applies.
    private static int CheckDigit(int[] digits, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++) sum += digits[i] * weights[i];
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
