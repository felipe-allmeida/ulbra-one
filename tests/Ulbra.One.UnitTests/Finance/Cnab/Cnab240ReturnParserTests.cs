using System.Reflection;
using System.Text;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain.Cnab;
using Ulbra.One.Finance.Infrastructure.Cnab;
using Xunit;

namespace Ulbra.One.UnitTests.Finance.Cnab;

/// <summary>
/// Tests for <see cref="Cnab240ReturnParser"/> and <see cref="CnabRecordReader"/>.
///
/// The golden .RET fixture is built inline using <see cref="CnabRecordBuilder"/> (the same writer
/// used in production remittances) so field positions are exact and byte-identical to real files.
/// Layout positions are 1-based as defined in <c>docs/cnab240-layout.md</c>.
/// </summary>
public class Cnab240ReturnParserTests
{
    // ---------------------------------------------------------------------------
    // CnabRecordReader unit tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Reader_Alpha_returns_trimmed_slice()
    {
        var line = BuildLine(b => b.Alpha(74, 20, "00000100001").Blank(1, 73).Blank(94, 147));
        var reader = new CnabRecordReader(line);
        Assert.Equal("00000100001", reader.Alpha(74, 20));
    }

    [Fact]
    public void Reader_Numeric_parses_right_justified_zero_padded_field()
    {
        var line = BuildLine(b => b.Numeric(1, 240, "0"));
        // Override specific field: put "000000042" in positions 1-9.
        var chars = line.ToCharArray();
        var val = "000000042";
        val.CopyTo(0, chars, 0, val.Length);
        var reader = new CnabRecordReader(new string(chars));
        Assert.Equal(42L, reader.Numeric(1, 9));
    }

    [Fact]
    public void Reader_Money_divides_by_100()
    {
        // pos 163-177 = 15 digits; "000000000150000" → 1500.00m
        var line = BuildLine(b => b.Blank(1, 162).Numeric(163, 15, "150000").Blank(178, 63));
        var reader = new CnabRecordReader(line);
        Assert.Equal(1500.00m, reader.Money(163, 15));
    }

    [Fact]
    public void Reader_Date_parses_ddmmaaaa()
    {
        // pos 155-162: "01062026" → 2026-06-01
        var line = BuildLine(b => b.Blank(1, 154).Date(155, 8, new DateOnly(2026, 6, 1)).Blank(163, 78));
        var reader = new CnabRecordReader(line);
        Assert.Equal(new DateOnly(2026, 6, 1), reader.Date(155, 8));
    }

    [Fact]
    public void Reader_Date_returns_null_when_all_zeros()
    {
        // All-zero date = not yet settled (remessa default).
        var line = BuildLine(b => b.Blank(1, 154).Numeric(155, 8, "0").Blank(163, 78));
        var reader = new CnabRecordReader(line);
        Assert.Null(reader.Date(155, 8));
    }

    [Fact]
    public void Reader_Date_returns_null_when_all_spaces()
    {
        var line = BuildLine(b => b.Blank(1, 240));
        var reader = new CnabRecordReader(line);
        Assert.Null(reader.Date(155, 8));
    }

    [Fact]
    public void Reader_rejects_line_not_240_chars()
    {
        Assert.Throws<ArgumentException>(() => new CnabRecordReader("short"));
    }

    // ---------------------------------------------------------------------------
    // Cnab240ReturnParser golden-file tests
    // ---------------------------------------------------------------------------

    // Fixture constants
    private const string RefPaid = "00004200001         ";     // 20-char reference, left-justified
    private const string RefRejected = "00004200002         "; // 20-char reference, left-justified
    private const string BankRefPaid = "BANCO000000000000123"; // 20-char Nosso Número (bank sets on retorno)
    private const string BankRefRejected = "BANCO000000000000456";

    // Occurrence codes written into pos 231-240 (10 chars = up to 5×2-char codes):
    // "BD        " = one paid code + 4 blank slots
    // "AG        " = one reject code + 4 blank slots
    private const string OccPaid = "BD        ";
    private const string OccRejected = "AG        ";

    // Settled values written into pos 163-177 (15 digits, 2 implied decimals)
    private const decimal PaidAmount = 1234.56m;    // → "000000000123456"
    private const decimal RejectedAmount = 0m;      // → "000000000000000" (rejected, not settled)

    // Settled dates: paid on 01/06/2026; rejected → zeros
    private static readonly DateOnly PaidDate = new(2026, 6, 1);

    /// <summary>Build and parse a minimal .RET with 1 paid Segmento A + 1 rejected Segmento A.</summary>
    private static IReadOnlyList<CnabReturnEntry> ParseGoldenFile()
    {
        var parser = new Cnab240ReturnParser();
        var bytes = BuildReturnFile();
        return parser.Parse(bytes);
    }

    [Fact]
    public void Parser_extracts_two_entries_from_golden_file()
    {
        Assert.Equal(2, ParseGoldenFile().Count);
    }

    [Fact]
    public void Parser_sets_Reference_from_SeuNumero_pos74to93()
    {
        var entries = ParseGoldenFile();
        Assert.Equal(RefPaid.Trim(), entries[0].Reference);
        Assert.Equal(RefRejected.Trim(), entries[1].Reference);
    }

    [Fact]
    public void Parser_sets_BankReference_from_NossoNumero_pos135to154()
    {
        var entries = ParseGoldenFile();
        Assert.Equal(BankRefPaid.Trim(), entries[0].BankReference);
        Assert.Equal(BankRefRejected.Trim(), entries[1].BankReference);
    }

    [Fact]
    public void Parser_sets_ActualValue_from_pos163to177()
    {
        var entries = ParseGoldenFile();
        Assert.Equal(PaidAmount, entries[0].ActualValue);
        Assert.Equal(RejectedAmount, entries[1].ActualValue);
    }

    [Fact]
    public void Parser_sets_ActualDate_from_pos155to162()
    {
        var entries = ParseGoldenFile();
        Assert.Equal(PaidDate, entries[0].ActualDate);
        Assert.Null(entries[1].ActualDate); // zeros → null for rejected entry
    }

    [Fact]
    public void Parser_sets_FavoredName_from_pos44to73()
    {
        // Favored name (Seg A 44-73) — shown by the crítica report.
        var entries = ParseGoldenFile();
        Assert.Equal("FORNECEDOR TESTE LTDA", entries[0].FavoredName);
    }

    [Fact]
    public void Parser_sets_LaunchValue_from_pos120to134_even_when_real_value_is_zero()
    {
        // Crítica case: the rejected entry has ActualValue 0 (not settled) but LaunchValue carries the
        // requested amount (Valor do Lançamento, pos 120-134) — what the crítica report shows.
        var entries = ParseGoldenFile();
        Assert.Equal(1234.56m, entries[0].LaunchValue);
        Assert.Equal(0m, entries[1].ActualValue);
        Assert.Equal(1234.56m, entries[1].LaunchValue);
    }

    [Fact]
    public void Parser_extracts_occurrence_codes_dropping_blank_pairs()
    {
        var entries = ParseGoldenFile();

        // OccPaid = "BD        " → codes = ["BD"]; the 4 blank pairs are dropped
        Assert.Equal(new[] { "BD" }, entries[0].OccurrenceCodes);

        // OccRejected = "AG        " → codes = ["AG"]
        Assert.Equal(new[] { "AG" }, entries[1].OccurrenceCodes);
    }

    [Fact]
    public void Parser_skips_non_detalhe_records_and_segmento_b_records()
    {
        // The golden file contains file header (0), lote header (1), 2×SegA (3A), 2×SegB (3B),
        // lote trailer (5), file trailer (9). Parser should yield exactly 2 entries.
        Assert.Equal(2, ParseGoldenFile().Count);
    }

    [Fact]
    public void Parser_tolerates_lf_only_line_endings()
    {
        // Replace CRLF with bare LF to test the split fallback.
        var crlf = Encoding.Latin1.GetString(BuildReturnFile());
        var lf = crlf.Replace("\r\n", "\n");
        var lfBytes = Encoding.Latin1.GetBytes(lf);
        var parser = new Cnab240ReturnParser();
        var entries = parser.Parse(lfBytes);
        Assert.Equal(2, entries.Count);
    }

    // ---------------------------------------------------------------------------
    // SantanderCnabProfile.OutcomeFor tests
    // ---------------------------------------------------------------------------

    private static readonly SantanderCnabProfile Profile = new();

    [Theory]
    [InlineData("00")]
    [InlineData("03")] // "Débito Autorizado pela Agência - Efetuado"
    [InlineData("BD")] // "Inclusão Efetuada com Sucesso"
    [InlineData("BE")] // AEL-918: paridade com o orcamento — "Alteração Efetuada com Sucesso"
    [InlineData("BF")] // AEL-918: paridade com o orcamento — "Exclusão Efetuada com Sucesso"
    public void OutcomeFor_paid_codes_returns_Paid(string code)
    {
        Assert.Equal(ReturnOutcome.Paid, Profile.OutcomeFor(new[] { code }));
    }

    [Theory]
    [InlineData("AG")] // Agência/Conta Corrente/DV Inválido
    [InlineData("01")] // Insuficiência de Fundos - Débito Não Efetuado
    [InlineData("02")] // Crédito ou Débito Cancelado pelo Pagador/Credor
    [InlineData("AB")] // Tipo de Operação Inválido
    [InlineData("AC")] // Tipo de Serviço Inválido
    [InlineData("PA")] // Pix não efetivado
    [InlineData("PF")] // CPF/CNPJ do recebedor inconsistente com o titular da conta
    public void OutcomeFor_reject_codes_returns_Rejected(string code)
    {
        Assert.Equal(ReturnOutcome.Rejected, Profile.OutcomeFor(new[] { code }));
    }

    /// <summary>
    /// Codes that are neither a known settlement nor a known rejection fall to
    /// <see cref="ReturnOutcome.Unknown"/> (manual review) — the fail-safe default (AEL-918): unlike the
    /// legado, an unknown code is NOT auto-rejected (never releases the installment). <c>05</c>/<c>06</c>/
    /// <c>09</c>/<c>10</c> aren't in the payment table (they came from cobrança, AEL-797); <c>EB</c> is a
    /// phantom code absent from the table (likely a transposition of <c>BE</c>). (<c>BE</c>/<c>BF</c> are
    /// promoted to Paid for legado parity — see <see cref="OutcomeFor_paid_codes_returns_Paid"/>.)
    /// </summary>
    [Theory]
    [InlineData("05")]
    [InlineData("06")]
    [InlineData("09")]
    [InlineData("10")]
    [InlineData("EB")] // código fantasma (não existe na tabela) — não é liquidação
    [InlineData("ZZ")] // código desconhecido qualquer — revisão, nunca auto-rejeita
    public void OutcomeFor_unverified_codes_fall_back_to_Unknown_instead_of_Rejected(string code)
    {
        Assert.Equal(ReturnOutcome.Unknown, Profile.OutcomeFor(new[] { code }));
    }

    [Fact]
    public void OutcomeFor_reject_dominates_paid()
    {
        // A file may include both a success and a reject — reject wins.
        Assert.Equal(ReturnOutcome.Rejected, Profile.OutcomeFor(new[] { "BD", "AG" }));
    }

    [Fact]
    public void OutcomeFor_unknown_code_returns_Unknown()
    {
        Assert.Equal(ReturnOutcome.Unknown, Profile.OutcomeFor(new[] { "ZZ" }));
    }

    [Fact]
    public void OutcomeFor_empty_list_returns_Paid()
    {
        // AEL-918: ocorrência em branco = liquidado (como o orcamento). A guarda de valor exato no
        // serviço (ActualValue == Amount) protege contra vazio com valor 0 (cai em divergente).
        Assert.Equal(ReturnOutcome.Paid, Profile.OutcomeFor(Array.Empty<string>()));
    }

    [Fact]
    public void Classified_codes_all_exist_in_the_occurrence_table()
    {
        // Guard against the EB-class bug (AEL-913): a code the profile classifies as Paid/Rejected but
        // which has no description in CnabReturnCodes. Reads the private policy sets by reflection so any
        // future addition to either set is covered automatically.
        foreach (var fieldName in new[] { "SantanderPaidCodes", "SantanderRejectedCodes" })
        {
            var field = typeof(SantanderCnabProfile).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.True(field is not null, $"Field {fieldName} not found on SantanderCnabProfile.");
            var codes = (IReadOnlySet<string>)field!.GetValue(null)!;
            Assert.NotEmpty(codes);
            foreach (var code in codes)
                Assert.True(CnabReturnCodes.Describe(code) is not null,
                    $"Occurrence code '{code}' in {fieldName} has no description in CnabReturnCodes.");
        }
    }

    [Fact]
    public void ParseHeader_reads_payer_document_agency_and_account()
    {
        // File Header (tipo 0): CNPJ 19-32, agência 53-57, DV 58, conta 59-70. Used to cross-check the
        // paying account the operator selected on the return-reconciliation screen.
        var header = new CnabRecordBuilder(240);
        header.Numeric(1, 3, "033");
        header.Numeric(4, 4, "0");
        header.Numeric(8, 1, "0");                 // tipo registro 0 = File Header
        header.Blank(9, 9);
        header.Numeric(18, 1, "2");
        header.Numeric(19, 14, "92814703000150");  // CNPJ pagador
        header.Blank(33, 20);
        header.Numeric(53, 5, "1234");             // agência → "01234"
        header.Alpha(58, 1, "7");                  // DV agência
        header.Numeric(59, 12, "567890");          // conta → "000000567890"
        header.Blank(71, 170);
        var file = Encoding.Latin1.GetBytes(header.BuildComplete() + "\r\n");

        var h = new Cnab240ReturnParser().ParseHeader(file);

        Assert.NotNull(h);
        Assert.Equal("92814703000150", h!.PayerDocument);
        Assert.Equal("01234", h.Agency);
        Assert.Equal("7", h.AgencyDigit);
        Assert.Equal("000000567890", h.Account);
    }

    // ---------------------------------------------------------------------------
    // Golden .RET file builder (using CnabRecordBuilder for exact positions)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal well-formed CNAB240 return file:
    ///   Record 0 — Header de Arquivo (tipo_registro=0)
    ///   Record 1 — Header de Lote    (tipo_registro=1)
    ///   Record 3A — Segmento A "paid"
    ///   Record 3B — Segmento B (complement — skipped by parser)
    ///   Record 3A — Segmento A "rejected"
    ///   Record 3B — Segmento B (complement — skipped by parser)
    ///   Record 5  — Trailer de Lote  (tipo_registro=5)
    ///   Record 9  — Trailer de Arquivo (tipo_registro=9)
    /// Each record is 240 bytes + CRLF.
    /// </summary>
    private static byte[] BuildReturnFile()
    {
        var sb = new StringBuilder();

        sb.Append(BuildFileHeader()); sb.Append("\r\n");
        sb.Append(BuildBatchHeader()); sb.Append("\r\n");
        sb.Append(BuildSegmentA(RefPaid, BankRefPaid, PaidDate, PaidAmount, OccPaid, 1)); sb.Append("\r\n");
        sb.Append(BuildSegmentB(2)); sb.Append("\r\n");
        sb.Append(BuildSegmentA(RefRejected, BankRefRejected, null, RejectedAmount, OccRejected, 3)); sb.Append("\r\n");
        sb.Append(BuildSegmentB(4)); sb.Append("\r\n");
        sb.Append(BuildBatchTrailer()); sb.Append("\r\n");
        sb.Append(BuildFileTrailer()); sb.Append("\r\n");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string BuildFileHeader()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");         // banco
        b.Numeric(4, 4, "0");           // lote 0000
        b.Numeric(8, 1, "0");           // tipo_registro 0
        b.Blank(9, 9);
        b.Numeric(18, 1, "2");          // CNPJ
        b.Numeric(19, 14, "0");
        b.Alpha(33, 20, "0033123498765432"); // convenio
        b.Numeric(53, 5, "0");
        b.Blank(58, 1);
        b.Numeric(59, 12, "0");
        b.Alpha(71, 1, "0");
        b.Blank(72, 1);
        b.Alpha(73, 30, "ULBRA EDUCACAO");
        b.Alpha(103, 30, "BANCO SANTANDER");
        b.Blank(133, 10);
        b.Numeric(143, 1, "2");         // retorno
        b.Numeric(144, 8, "25062026");  // data
        b.Numeric(152, 6, "143000");    // hora
        b.Numeric(158, 6, "1");         // NSA
        b.Numeric(164, 3, "060");
        b.Numeric(167, 5, "0");
        b.Blank(172, 20);
        b.Blank(192, 20);
        b.Blank(212, 19);
        b.Blank(231, 10);               // occurrencias (arquivo)
        return b.BuildComplete();
    }

    private static string BuildBatchHeader()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");           // lote 0001
        b.Numeric(8, 1, "1");           // tipo_registro 1
        b.Alpha(9, 1, "C");
        b.Numeric(10, 2, "20");
        b.Numeric(12, 2, "03");
        b.Numeric(14, 3, "031");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Alpha(33, 20, "0033123498765432");
        b.Numeric(53, 5, "0");
        b.Blank(58, 1);
        b.Numeric(59, 12, "0");
        b.Alpha(71, 1, "0");
        b.Blank(72, 1);
        b.Alpha(73, 30, "ULBRA EDUCACAO");
        b.Blank(103, 40);
        b.Blank(143, 30);
        b.Numeric(173, 5, "0");
        b.Blank(178, 15);
        b.Blank(193, 20);
        b.Numeric(213, 5, "0");
        b.Numeric(218, 3, "0");
        b.Blank(221, 2);
        b.Blank(223, 8);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    /// <summary>
    /// Build a Segmento-A return record. The occurrence field at 231-240 is written verbatim
    /// (already padded to exactly 10 chars in the constants above).
    /// </summary>
    private static string BuildSegmentA(
        string reference,
        string bankRef,
        DateOnly? actualDate,
        decimal actualValue,
        string occurrences10,
        int seq)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");                              // tipo_registro 3
        b.Numeric(9, 5, seq);                              // seq in lote
        b.Alpha(14, 1, "A");                               // segmento A
        b.Numeric(15, 1, "0");                             // tipo_movimento
        b.Numeric(16, 2, "0");                             // instrucao
        b.Numeric(18, 3, "18");                            // camara TED
        b.Numeric(21, 3, "341");                           // banco favorecido
        b.Numeric(24, 5, "455");                           // agencia
        b.Blank(29, 1);                                    // dv agencia
        b.Numeric(30, 12, "12345678");                     // conta
        b.Alpha(42, 1, "9");                               // dv conta
        b.Blank(43, 1);
        b.Alpha(44, 30, "FORNECEDOR TESTE LTDA");          // nome favorecido
        // "Seu Número" — reference at 74-93 (20 chars, left-justified, space-padded).
        b.Alpha(74, 20, reference.Trim(), allowTruncate: false); // write trimmed; builder left-pads
        b.Date(94, 8, new DateOnly(2026, 7, 1));           // data_pagamento (scheduled)
        b.Alpha(102, 3, "BRL");
        b.Numeric(105, 15, "0");                           // qtd moeda
        b.Money(120, 15, 1234.56m);                        // valor_pagamento (original)
        // "Nosso Número" at 135-154 (bank reference on return).
        b.Alpha(135, 20, bankRef.Trim(), allowTruncate: false);
        // Data real at 155-162.
        if (actualDate.HasValue)
            b.Date(155, 8, actualDate.Value);
        else
            b.Numeric(155, 8, "0");                        // zeros = not settled
        // Valor real at 163-177.
        b.Money(163, 15, actualValue);
        b.Blank(178, 40);
        b.Blank(218, 2);
        b.Alpha(220, 5, "00001");                          // finalidade TED
        b.Blank(225, 2);
        b.Blank(227, 3);
        b.Numeric(230, 1, "0");                            // aviso
        // Occurrence codes at 231-240 (10 chars).
        b.Alpha(231, 10, occurrences10.Trim(), allowTruncate: true);
        return b.BuildComplete();
    }

    /// <summary>Minimal Segmento-B record (skipped by the parser but required for valid file structure).</summary>
    private static string BuildSegmentB(int seq)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");           // tipo_registro 3
        b.Numeric(9, 5, seq);
        b.Alpha(14, 1, "B");            // segmento B — parser skips this
        b.Numeric(15, 2, "0");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Blank(33, 35);
        b.Blank(68, 60);
        b.Blank(128, 99);
        b.Blank(227, 6);
        b.Blank(233, 8);
        return b.BuildComplete();
    }

    private static string BuildBatchTrailer()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "5");           // tipo_registro 5
        b.Blank(9, 9);
        b.Numeric(18, 6, "6");          // hdr + 2×segA + 2×segB + trl = 6
        b.Money(24, 18, 1234.56m);
        b.Numeric(42, 18, "0");
        b.Numeric(60, 6, "0");
        b.Blank(66, 165);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    private static string BuildFileTrailer()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "9999");
        b.Numeric(8, 1, "9");           // tipo_registro 9
        b.Blank(9, 9);
        b.Numeric(18, 6, "1");          // 1 lote
        b.Numeric(24, 6, "8");          // 8 records
        b.Blank(30, 6);
        b.Blank(36, 205);
        return b.BuildComplete();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Build a 240-char test line from a lambda that fills exactly all 240 positions.
    /// Used for CnabRecordReader unit tests.
    /// </summary>
    private static string BuildLine(Action<CnabRecordBuilder> configure)
    {
        var b = new CnabRecordBuilder(240);
        configure(b);
        return b.Build(); // plain Build — we may leave some positions unfilled in individual tests
    }
}
