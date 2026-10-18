using Ulbra.One.Finance.Application.Endpoints.Payables;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class BoletoEntryValidationTests
{
    private static readonly string Valid47 = new('1', 47);
    // A 48-digit line is only valid as arrecadação/convênio, which always begins with 8.
    private static readonly string Valid48 = "8" + new string('2', 47);
    private static readonly string Invalid46 = new('3', 46);
    private static readonly string Invalid48NoLeadEight = "3" + new string('1', 47);

    [Fact]
    public void All_valid_lines_are_allowed()
        => Assert.Null(BoletoEntryValidation.Problem(new[] { Valid47, Valid48 }));

    [Fact]
    public void A_provided_wrong_length_line_is_rejected()
        => Assert.NotNull(BoletoEntryValidation.Problem(new[] { Valid47, Invalid46 }));

    [Fact]
    public void A_48_digit_line_without_the_8_prefix_is_rejected()
        => Assert.NotNull(BoletoEntryValidation.Problem(new[] { Invalid48NoLeadEight }));

    [Fact]
    public void Blank_or_null_lines_are_allowed()
        => Assert.Null(BoletoEntryValidation.Problem(new string?[] { null, "", "   " }));
}
