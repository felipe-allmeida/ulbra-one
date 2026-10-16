using Ulbra.One.Common.Domain.Validation;
using Xunit;

namespace Ulbra.One.UnitTests.Common;

/// <summary>The single choke-point that canonicalizes LC 116 subitems to "N.NN" across both NFS-e parsers and the
/// withholding-rule editor, so a rule keyed on "1.07" matches whatever form the invoice/UI supplied.</summary>
public class Lc116Tests
{
    [Theory]
    [InlineData("1.07", "1.07")]
    [InlineData("01.07", "1.07")]   // ABRASF padded item
    [InlineData("0107", "1.07")]    // ABRASF undotted (or cTribNac first-4)
    [InlineData("  17.01  ", "17.01")]
    [InlineData("11.02", "11.02")]
    [InlineData("07.02", "7.02")]
    [InlineData("1.070", "1.07")]   // over-long subitem truncated to 2
    public void Canonicalizes_common_variants(string raw, string expected) =>
        Assert.Equal(expected, Lc116.Canonical(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.7")]   // 1-digit subitem is malformed → not fabricated
    [InlineData("7")]     // too short to split
    public void Returns_null_for_blank_or_malformed(string? raw) =>
        Assert.Null(Lc116.Canonical(raw));
}
