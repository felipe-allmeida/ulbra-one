using System.Text;
using Ulbra.One.Finance.Infrastructure.Cnab;
using Xunit;

namespace Ulbra.One.UnitTests.Finance.Cnab;

/// <summary>
/// Return-parser coverage for boleto (Segmento J), convênio (Segmento O) and the authentication
/// code (Segmento Z). Positions 1-based per <c>docs/cnab240-layout.md</c>. Fixtures are built with
/// the production <see cref="CnabRecordBuilder"/> so field positions are byte-exact.
/// </summary>
public class Cnab240ReturnParserSegmentTests
{
    private static readonly Cnab240ReturnParser Parser = new();

    private static byte[] File(params string[] records)
    {
        var sb = new StringBuilder();
        foreach (var r in records)
        {
            Assert.Equal(240, r.Length);
            sb.Append(r).Append("\r\n");
        }
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string SegmentA(string reference, string bankRef, DateOnly payDate, decimal value, string occ10)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, 1);
        b.Alpha(14, 1, "A");
        b.Numeric(15, 1, "0");
        b.Numeric(16, 2, "0");
        b.Numeric(18, 3, "18");
        b.Numeric(21, 3, "341");
        b.Numeric(24, 5, "455");
        b.Blank(29, 1);
        b.Numeric(30, 12, "12345678");
        b.Alpha(42, 1, "9");
        b.Blank(43, 1);
        b.Alpha(44, 30, "FORNECEDOR");
        b.Alpha(74, 20, reference);             // "Seu Número"
        b.Date(94, 8, new DateOnly(2026, 7, 1));
        b.Alpha(102, 3, "BRL");
        b.Numeric(105, 15, "0");
        b.Money(120, 15, value);
        b.Alpha(135, 20, bankRef);              // "Nosso Número"
        b.Date(155, 8, payDate);                // data real
        b.Money(163, 15, value);                // valor real
        b.Blank(178, 40);
        b.Blank(218, 2);
        b.Alpha(220, 5, "00001");
        b.Blank(225, 2);
        b.Blank(227, 3);
        b.Numeric(230, 1, "0");
        b.Alpha(231, 10, occ10, allowTruncate: true);
        return b.BuildComplete();
    }

    private static string SegmentB(string favoredDoc)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, 2);
        b.Alpha(14, 1, "B");
        b.Numeric(15, 2, "0");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, favoredDoc);          // CPF/CNPJ favorecido (parser fallback key)
        b.Blank(33, 208);
        return b.BuildComplete();
    }

    private static string SegmentJ(string reference, string bankRef, DateOnly payDate, decimal payValue, string occ10, string barcode = "0")
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, 1);
        b.Alpha(14, 1, "J");
        b.Numeric(15, 1, "0");
        b.Numeric(16, 2, "0");
        b.Numeric(18, 44, barcode);             // código de barras (44) — read as CnabReturnEntry.Barcode
        b.Alpha(62, 30, "CEDENTE BOLETO");
        b.Date(92, 8, new DateOnly(2026, 7, 5));// vencimento
        b.Money(100, 15, payValue);             // valor nominal
        b.Numeric(115, 15, "0");
        b.Numeric(130, 15, "0");
        b.Date(145, 8, payDate);                // data pagamento (parser reads here)
        b.Money(153, 15, payValue);             // valor pagamento (parser reads here)
        b.Numeric(168, 15, "0");
        b.Alpha(183, 20, reference);            // "Seu Número"
        b.Alpha(203, 20, bankRef);              // "Nosso Número"
        b.Numeric(223, 2, "09");
        b.Blank(225, 6);
        b.Alpha(231, 10, occ10, allowTruncate: true);
        return b.BuildComplete();
    }

    /// <summary>Segmento J-52 (sacado/cedente companion): segment 'J' with "52" at 18-19. Carries no
    /// settlement — <paramref name="ghostValueAt153"/> sits where a buggy Seg-J parse would read the
    /// "valor pagamento", so a regression that double-counts it is caught.</summary>
    private static string SegmentJ52(decimal ghostValueAt153)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, 2);
        b.Alpha(14, 1, "J");
        b.Numeric(15, 1, "0");
        b.Numeric(16, 2, "0");                  // código movimento (G026)
        b.Numeric(18, 2, "52");                 // identificação registro opcional = J-52
        b.Numeric(20, 56, "0");                 // pagador (20-75)
        b.Numeric(76, 56, "0");                 // beneficiário (76-131)
        b.Numeric(132, 21, "0");                // 132-152
        b.Money(153, 15, ghostValueAt153);      // 153-167 — where a mis-parse would sum a value
        b.Numeric(168, 63, "0");                // 168-230
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    private static string SegmentO(string reference, string bankRef, DateOnly payDate, decimal value, string occ10, string barcode = "8")
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, 1);
        b.Alpha(14, 1, "O");
        b.Numeric(15, 1, "0");
        b.Numeric(16, 2, "0");
        b.Numeric(18, 44, barcode);             // código de barras (44, convênio) — read as CnabReturnEntry.Barcode
        b.Alpha(62, 30, "CONCESSIONARIA");
        b.Date(92, 8, new DateOnly(2026, 7, 6));// vencimento
        b.Date(100, 8, payDate);                // data pagamento (parser reads here)
        b.Money(108, 15, value);                // valor (parser reads here)
        b.Alpha(123, 20, reference);            // "Seu Número"
        b.Alpha(143, 20, bankRef);              // "Nosso Número"
        b.Blank(163, 68);
        b.Alpha(231, 10, occ10, allowTruncate: true);
        return b.BuildComplete();
    }

    private static string SegmentZ(string authCode)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, 2);
        b.Alpha(14, 1, "Z");
        b.Alpha(15, 64, authCode);              // autenticação
        b.Blank(79, 162);
        return b.BuildComplete();
    }

    private static string BatchTrailer(decimal declaredTotal)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "5");                   // tipo_registro 5 = trailer de lote
        b.Blank(9, 9);
        b.Numeric(18, 6, "3");
        b.Money(24, 18, declaredTotal);         // somatória dos valores
        b.Numeric(42, 18, "0");
        b.Numeric(60, 6, "0");
        b.Blank(66, 165);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    [Fact]
    public void Parses_segment_J_boleto_fields_and_attaches_segment_Z_auth()
    {
        var file = File(
            SegmentJ("00007700001", "NOSSOJ123", new DateOnly(2026, 7, 10), 500.00m, "00        "),
            SegmentZ("AUT1234567890"));

        var entries = Parser.Parse(file);

        var e = Assert.Single(entries);
        Assert.Equal("00007700001", e.Reference);
        Assert.Equal(500.00m, e.ActualValue);
        Assert.Equal(new DateOnly(2026, 7, 10), e.ActualDate);
        Assert.Equal("NOSSOJ123", e.BankReference);
        Assert.Equal("AUT1234567890", e.AuthCode);
        Assert.Contains("00", e.OccurrenceCodes);
    }

    [Fact]
    public void Segment_J52_companion_is_skipped_not_parsed_as_a_payment()
    {
        // A boleto retorno echoes a Seg J then its J-52 (sacado/cedente) companion. Only the J is a
        // payment; the J-52 (segment 'J' + "52" at 18-19) must not become a second, spurious entry.
        var file = File(
            SegmentJ("00007700001", "NOSSOJ123", new DateOnly(2026, 7, 10), 500.00m, "00        "),
            SegmentJ52(777.00m),
            SegmentZ("AUTJ52TEST"));

        var e = Assert.Single(Parser.Parse(file));
        Assert.Equal("00007700001", e.Reference);
        Assert.Equal(500.00m, e.ActualValue);
        Assert.Equal("AUTJ52TEST", e.AuthCode); // Seg Z still attaches to the real Seg J
    }

    [Fact]
    public void CheckTrailers_does_not_double_count_the_J52_companion()
    {
        // Bug guard: if the J-52 were summed as a Seg J, actual would be 500+777=1277 and falsely diverge.
        var file = File(
            SegmentJ("00007700001", "NOSSOJ123", new DateOnly(2026, 7, 10), 500.00m, "00        "),
            SegmentJ52(777.00m),
            BatchTrailer(500.00m));

        var check = Assert.Single(Parser.CheckTrailers(file));
        Assert.Equal(500.00m, check.ActualTotal);
        Assert.False(check.IsDivergent);
    }

    [Fact]
    public void Segment_A_captures_favored_document_from_following_segment_B()
    {
        var file = File(
            SegmentA("00004200001", "NOSSOA1", new DateOnly(2026, 7, 3), 1234.56m, "00        "),
            SegmentB("11222333000181"));

        var e = Assert.Single(Parser.Parse(file));
        Assert.Equal("00004200001", e.Reference);
        Assert.Equal("11222333000181", e.FavoredDocument);
    }

    [Fact]
    public void Parses_segment_O_convenio_fields()
    {
        var file = File(
            SegmentO("00007700002", "NOSSOO456", new DateOnly(2026, 7, 11), 89.90m, "00        "));

        var entries = Parser.Parse(file);

        var e = Assert.Single(entries);
        Assert.Equal("00007700002", e.Reference);
        Assert.Equal(89.90m, e.ActualValue);
        Assert.Equal(new DateOnly(2026, 7, 11), e.ActualDate);
        Assert.Equal("NOSSOO456", e.BankReference);
        Assert.Null(e.AuthCode); // no Segmento Z
    }

    [Fact]
    public void Parses_segment_J_barcode_for_fallback_matching()
    {
        // The 44-position código de barras (pos 18-61) is read into CnabReturnEntry.Barcode so the
        // service can fall back to matching a boleto line by barcode + value.
        var barcode = "0339" + new string('1', 40); // 44 digits, boleto bancário
        var file = File(SegmentJ("00007700001", "NOSSOJ123", new DateOnly(2026, 7, 10), 500.00m, "00        ", barcode));

        var e = Assert.Single(Parser.Parse(file));
        Assert.Equal(barcode, e.Barcode);
    }

    [Fact]
    public void Parses_segment_O_barcode_for_fallback_matching()
    {
        var barcode = "8" + new string('2', 43); // 44 digits, convênio/arrecadação (starts with 8)
        var file = File(SegmentO("00007700002", "NOSSOO456", new DateOnly(2026, 7, 11), 89.90m, "00        ", barcode));

        var e = Assert.Single(Parser.Parse(file));
        Assert.Equal(barcode, e.Barcode);
    }

    [Fact]
    public void Segment_A_has_no_barcode()
    {
        var file = File(SegmentA("00004200001", "NOSSOA1", new DateOnly(2026, 7, 3), 10m, "00        "));
        var e = Assert.Single(Parser.Parse(file));
        Assert.Null(e.Barcode);
    }

    [Fact]
    public void Segment_Z_without_a_preceding_payment_is_ignored()
    {
        var entries = Parser.Parse(File(SegmentZ("ORPHAN")));
        Assert.Empty(entries);
    }

    [Fact]
    public void CheckTrailers_flags_divergence_when_declared_total_mismatches_summed_details()
    {
        var file = File(
            SegmentJ("00007700001", "NOSSOJ123", new DateOnly(2026, 7, 10), 500.00m, "00        "),
            BatchTrailer(999.00m)); // declared ≠ summed 500.00

        var check = Assert.Single(Parser.CheckTrailers(file));
        Assert.Equal("0001", check.Lote);
        Assert.Equal(999.00m, check.DeclaredTotal);
        Assert.Equal(500.00m, check.ActualTotal);
        Assert.True(check.IsDivergent);
    }

    [Fact]
    public void CheckTrailers_matches_when_declared_equals_summed_details()
    {
        var file = File(
            SegmentJ("00007700001", "NOSSOJ123", new DateOnly(2026, 7, 10), 500.00m, "00        "),
            BatchTrailer(500.00m));

        var check = Assert.Single(Parser.CheckTrailers(file));
        Assert.False(check.IsDivergent);
    }
}
