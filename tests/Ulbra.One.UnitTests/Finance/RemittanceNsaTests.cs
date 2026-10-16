using Ulbra.One.Finance.Application.Remittances;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// <see cref="RemittanceNsa"/>: the per-environment file NSA. Santander treats NSA 1-10 as TEST (note
/// G010); production needs ≥ 11. The persisted sequence stays monotonic/unique.
/// </summary>
public class RemittanceNsaTests
{
    [Theory]
    [InlineData(1, 11)]
    [InlineData(2, 12)]
    [InlineData(10, 20)]
    [InlineData(178725, 178735)]
    public void Production_offsets_by_ten_so_nsa_is_always_at_least_11(int seq, int expected)
    {
        Assert.Equal(expected, RemittanceNsa.ForFile(seq, isProduction: true));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    [InlineData(11, 1)]   // cycles back into the test range
    [InlineData(12, 2)]
    [InlineData(20, 10)]
    [InlineData(21, 1)]
    public void Test_cycles_within_the_1_to_10_range(int seq, int expected)
    {
        var nsa = RemittanceNsa.ForFile(seq, isProduction: false);
        Assert.Equal(expected, nsa);
        Assert.InRange(nsa, 1, 10);
    }

    [Fact]
    public void Production_never_lands_in_the_test_range()
    {
        for (var seq = 1; seq <= 50; seq++)
            Assert.True(RemittanceNsa.ForFile(seq, isProduction: true) >= 11);
    }
}
