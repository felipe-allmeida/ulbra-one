using Ulbra.One.Finance.Infrastructure.Cnab;
using Xunit;

namespace Ulbra.One.UnitTests.Finance.Cnab;

public class CnabRecordBuilderTests
{
    [Fact]
    public void Alpha_left_justifies_pads_space_uppercases_strips_accents()
    {
        Assert.Equal("JOSE      ", new CnabRecordBuilder(10).Alpha(1, 10, "José").Build());
    }

    [Fact]
    public void Numeric_right_justifies_zero_pads()
    {
        Assert.Equal("0000000042", new CnabRecordBuilder(10).Numeric(1, 10, 42).Build());
    }

    [Fact]
    public void Money_two_implied_decimals_no_separator()
    {
        Assert.Equal("000000123456", new CnabRecordBuilder(12).Money(1, 12, 1234.56m).Build());
    }

    [Fact]
    public void Date_ddmmyyyy()
    {
        Assert.Equal("01062026", new CnabRecordBuilder(8).Date(1, 8, new DateOnly(2026, 6, 1)).Build());
    }

    [Fact]
    public void Alpha_overflow_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new CnabRecordBuilder(3).Alpha(1, 3, "ABCD"));
    }

    [Fact]
    public void Numeric_overflow_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new CnabRecordBuilder(3).Numeric(1, 3, 1234));
    }

    [Fact]
    public void Build_enforces_exact_length()
    {
        Assert.Equal(240, new CnabRecordBuilder(240).Build().Length);
    }

    [Fact]
    public void Alpha_truncates_when_explicitly_allowed()
    {
        // Non-strict alpha may right-trim overflow (e.g. long favorecido names).
        Assert.Equal("ABC", new CnabRecordBuilder(3).Alpha(1, 3, "ABCDEF", allowTruncate: true).Build());
    }

    [Fact]
    public void AlphaStrict_never_truncates_the_reference_field_it_throws()
    {
        // "Seu Número" (Reference) is the round-trip key — silent truncation would break the match.
        var reference = new string('X', 21); // one over the 20-char field
        Assert.Throws<InvalidOperationException>(() => new CnabRecordBuilder(240).AlphaStrict(74, 20, reference));
    }

    [Fact]
    public void Numeric_truncates_leading_digits_when_explicitly_allowed()
    {
        // Informational numeric fields (address número/CEP) keep the leading digits and drop the
        // least-significant tail instead of throwing — a bad address never aborts the whole file.
        Assert.Equal("12345", new CnabRecordBuilder(5).Numeric(1, 5, "123456789", allowTruncate: true).Build());
    }

    [Fact]
    public void Numeric_overflow_still_throws_by_default()
    {
        // Without the explicit opt-in, overflow is a hard error (money, counters, account ids).
        Assert.Throws<InvalidOperationException>(() => new CnabRecordBuilder(5).Numeric(1, 5, "123456"));
    }

    [Fact]
    public void Numeric_string_digits_right_justifies_zero_pads()
    {
        Assert.Equal("0001234", new CnabRecordBuilder(7).Numeric(1, 7, "1234").Build());
    }

    [Fact]
    public void Numeric_string_strips_non_digits()
    {
        // Document/account fields arrive formatted (dots, slashes) — only digits survive.
        Assert.Equal("12345678000100", new CnabRecordBuilder(14).Numeric(1, 14, "12.345.678/0001-00").Build());
    }

    [Fact]
    public void Build_leaves_unwritten_positions_as_spaces()
    {
        // Plain Build() never validates coverage — it just renders, so the brief's
        // "new CnabRecordBuilder(240).Build().Length == 240" holds.
        Assert.Equal("AB   ", new CnabRecordBuilder(5).Alpha(1, 2, "AB").Build());
    }

    [Fact]
    public void BuildComplete_throws_when_not_fully_filled()
    {
        Assert.Throws<InvalidOperationException>(() => new CnabRecordBuilder(10).Alpha(1, 5, "AB").BuildComplete());
    }

    [Fact]
    public void Blank_fills_field_with_spaces()
    {
        Assert.Equal("     ", new CnabRecordBuilder(5).Blank(1, 5).Build());
    }

    [Fact]
    public void Overlapping_writes_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new CnabRecordBuilder(10).Alpha(1, 5, "AB").Alpha(3, 5, "CD"));
    }
}
