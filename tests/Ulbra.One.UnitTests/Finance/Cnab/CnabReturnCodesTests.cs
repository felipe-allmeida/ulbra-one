using Ulbra.One.Finance.Domain.Cnab;
using Xunit;

namespace Ulbra.One.UnitTests.Finance.Cnab;

public class CnabReturnCodesTests
{
    [Fact]
    public void Describe_returns_the_portuguese_text_for_a_known_code() =>
        Assert.Equal("Crédito ou Débito Efetivado", CnabReturnCodes.Describe("00"));

    [Fact]
    public void Describe_returns_null_for_an_unknown_code() =>
        Assert.Null(CnabReturnCodes.Describe("Q9"));

    [Theory]
    [InlineData(" 00 ")]
    [InlineData("00")]
    public void Describe_trims_the_raw_slice_off_the_file(string code) =>
        Assert.Equal("Crédito ou Débito Efetivado", CnabReturnCodes.Describe(code));

    [Fact]
    public void Describe_is_case_insensitive() =>
        Assert.Equal(CnabReturnCodes.Describe("AB"), CnabReturnCodes.Describe("ab"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Describe_handles_blank_input(string? code) => Assert.Null(CnabReturnCodes.Describe(code));

    // The two codes observed in the real .RET of 15/07/2026 that fell into Unknown for lack of a
    // description — the whole point of porting this table.
    [Theory]
    [InlineData("PA", "Pix não efetivado")]
    [InlineData("PF", "CPF/CNPJ do usuário recebedor não é consistente com o titular da conta transacional especificada")]
    public void Describes_the_pix_codes_seen_in_production(string code, string expected) =>
        Assert.Equal(expected, CnabReturnCodes.Describe(code));

    /// <summary>Guards the seed against an accidental truncation. 131 = the full occurrence table
    /// ported from the legacy app (verified code-by-code against its <c>CNAB_RETURN_CODES</c>).</summary>
    [Fact]
    public void Table_covers_the_whole_manual() => Assert.Equal(131, CnabReturnCodes.Count);

    // ---- Explain (conciliation notes) ----------------------------------------------------------

    [Fact]
    public void Explain_pairs_each_code_with_its_description() =>
        Assert.Equal("AB (Tipo de Operação Inválido)", CnabReturnCodes.Explain(["AB"]));

    [Fact]
    public void Explain_joins_multiple_codes() =>
        Assert.Equal(
            "AG (Agência/Conta Corrente/DV Inválido), AO (Nome do Favorecido não Informado)",
            CnabReturnCodes.Explain(["AG", "AO"]));

    [Fact]
    public void Explain_keeps_unknown_codes_bare_so_nothing_is_silently_dropped() =>
        Assert.Equal("Q9", CnabReturnCodes.Explain(["Q9"]));

    [Fact]
    public void Explain_mixes_known_and_unknown() =>
        Assert.Equal("AB (Tipo de Operação Inválido), Q9", CnabReturnCodes.Explain(["AB", "Q9"]));

    [Fact]
    public void Explain_skips_blank_entries() =>
        Assert.Equal("AB (Tipo de Operação Inválido)", CnabReturnCodes.Explain(["AB", "  ", ""]));

    [Theory]
    [InlineData(null)]
    public void Explain_returns_null_for_no_codes(IEnumerable<string>? codes) =>
        Assert.Null(CnabReturnCodes.Explain(codes));

    [Fact]
    public void Explain_returns_null_for_an_all_blank_list() =>
        Assert.Null(CnabReturnCodes.Explain(["", "   "]));
}
