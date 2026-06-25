using System.Text;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Infrastructure.Cnab;
using Xunit;

namespace Ulbra.One.UnitTests.Finance.Cnab;

/// <summary>
/// Golden-file test for the Santander 033 CNAB240 writer. A fixed input (1 TED + 1 PIX line →
/// 2 lotes) is generated, split on CRLF, and asserted position-by-position against the
/// layout in <c>.superpowers/sdd/cnab240-layout.md</c>. Positions below are 1-based (as in
/// the layout doc); <see cref="Field"/> converts to a 0-based substring.
/// </summary>
public class Cnab240RemittanceWriterTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 6, 24, 14, 30, 45, TimeSpan.Zero);

    private static readonly RemittancePayerInfo Payer = new(
        Cnpj: "92814703000150",
        LegalName: "ULBRA EDUCACAO",
        Agency: "1234",
        AccountNumber: "000000567890",
        AccountDigit: "1",
        AgreementCode: "987654321098");

    private static readonly RemittanceFileLine TedLine = new(
        Method: PaymentMethod.Ted,
        Amount: 1234.56m,
        PaymentDate: new DateOnly(2026, 7, 1),
        FavoredName: "Fornecedor Alpha LTDA",
        FavoredDocument: "11222333000181",
        FavoredBankCode: "341",
        FavoredAgency: "0455",
        FavoredAccount: "000012345678",
        FavoredAccountDigit: "9",
        FavoredPixKey: "",
        Reference: "00004200001");

    private static readonly RemittanceFileLine PixLine = new(
        Method: PaymentMethod.Pix,
        Amount: 50.00m,
        PaymentDate: new DateOnly(2026, 7, 2),
        FavoredName: "Beneficiario Beta ME",
        FavoredDocument: "55666777000122",
        FavoredBankCode: "",
        FavoredAgency: "",
        FavoredAccount: "",
        FavoredAccountDigit: "",
        FavoredPixKey: "beta@pix.example.com",
        Reference: "00004200002");

    private static RemittanceFileInput BuildInput() => new(
        BankCode: "033",
        SequenceNumber: 42,
        GeneratedAt: GeneratedAt,
        Payer: Payer,
        Lines: new[] { TedLine, PixLine });

    private static string[] Generate()
    {
        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        var bytes = writer.Generate(BuildInput());
        var text = Encoding.Latin1.GetString(bytes);
        Assert.EndsWith("\r\n", text);
        // CRLF-terminated; trailing terminator yields an empty final element we drop.
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>1-based field slice from a record, per the layout doc.</summary>
    private static string Field(string record, int start, int length) => record.Substring(start - 1, length);

    [Fact]
    public void File_has_header_two_lotes_each_four_records_and_trailer()
    {
        var r = Generate();
        // 0 + [1 + A + B + 5] (TED) + [1 + A + B + 5] (PIX) + 9 = 10 records.
        Assert.Equal(10, r.Length);
    }

    [Fact]
    public void Every_record_is_exactly_240_chars()
    {
        foreach (var record in Generate())
            Assert.Equal(240, record.Length);
    }

    [Fact]
    public void Bytes_are_latin1_and_crlf_terminated_with_no_trailing_record()
    {
        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        var bytes = writer.Generate(BuildInput());
        var text = Encoding.Latin1.GetString(bytes);

        Assert.EndsWith("\r\n", text);
        Assert.Equal(10 * 242, bytes.Length); // 240 + CRLF per record, nothing after the type-9 trailer
        Assert.DoesNotContain('\n', text.Replace("\r\n", string.Empty)); // no stray LFs
    }

    [Fact]
    public void Bank_code_033_from_profile_on_every_record()
    {
        var profile = new SantanderCnabProfile();
        foreach (var record in Generate())
            Assert.Equal(profile.BankCode, Field(record, 1, 3));
        Assert.Equal("033", profile.BankCode);
    }

    [Fact]
    public void File_header_carries_type_zero_nsa_date_and_convenio()
    {
        var header = Generate()[0];

        Assert.Equal("0000", Field(header, 4, 4));            // lote 0000
        Assert.Equal("0", Field(header, 8, 1));               // tipo registro 0
        Assert.Equal("2", Field(header, 18, 1));              // tipo inscricao CNPJ
        Assert.Equal("92814703000150", Field(header, 19, 14)); // CNPJ
        Assert.Equal("003312349876", Field(header, 33, 12));  // convenio prefix: 0033 + 1234 + 9876...
        Assert.Equal("987654321098", Field(header, 41, 12));  // convenio code (12 digits) at 41-52
        Assert.Equal("1", Field(header, 143, 1));             // remessa
        Assert.Equal("24062026", Field(header, 144, 8));      // data geracao DDMMYYYY
        Assert.Equal("143045", Field(header, 152, 6));        // hora HHMMSS
        Assert.Equal("000042", Field(header, 158, 6));        // NSA
        Assert.Equal("060", Field(header, 164, 3));           // versao layout
        Assert.Equal("BANCO SANTANDER", Field(header, 103, 15)); // nome banco (left-justified)
    }

    [Fact]
    public void Ted_lote_header_has_lote_0001_servico_20_forma_03()
    {
        var tedHeader = Generate()[1];

        Assert.Equal("0001", Field(tedHeader, 4, 4));  // lote 0001
        Assert.Equal("1", Field(tedHeader, 8, 1));     // tipo registro 1
        Assert.Equal("C", Field(tedHeader, 9, 1));     // operacao credito
        Assert.Equal("20", Field(tedHeader, 10, 2));   // servico fornecedores
        Assert.Equal("03", Field(tedHeader, 12, 2));   // forma TED = 03 (Santander)
        Assert.Equal("031", Field(tedHeader, 14, 3));  // versao layout lote
    }

    [Fact]
    public void Ted_segment_a_carries_favored_bank_camara_amount_and_reference_untruncated()
    {
        var segA = Generate()[2];

        Assert.Equal("0001", Field(segA, 4, 4));               // same lote
        Assert.Equal("3", Field(segA, 8, 1));                  // detalhe
        Assert.Equal("00001", Field(segA, 9, 5));              // seq within lote = 1
        Assert.Equal("A", Field(segA, 14, 1));                 // segmento A
        Assert.Equal("018", Field(segA, 18, 3));               // camara TED
        Assert.Equal("341", Field(segA, 21, 3));               // favored bank
        Assert.Equal("00455", Field(segA, 24, 5));             // favored agency
        Assert.Equal("000012345678", Field(segA, 30, 12));     // favored account
        Assert.Equal("9", Field(segA, 42, 1));                 // favored account DV
        Assert.Equal("FORNECEDOR ALPHA LTDA         ", Field(segA, 44, 30)); // name uppercased, padded
        Assert.Equal("00004200001         ", Field(segA, 74, 20)); // "Seu Numero" = Reference untruncated, left-justified
        Assert.Equal("01072026", Field(segA, 94, 8));          // payment date
        Assert.Equal("BRL", Field(segA, 102, 3));              // moeda
        Assert.Equal("000000000123456", Field(segA, 120, 15)); // valor 1234.56 → 123456
        Assert.Equal("00001", Field(segA, 220, 5));            // finalidade TED
    }

    [Fact]
    public void Ted_segment_b_carries_favored_inscription_and_blank_pix_key()
    {
        var segB = Generate()[3];

        Assert.Equal("00002", Field(segB, 9, 5));          // seq within lote = 2
        Assert.Equal("B", Field(segB, 14, 1));             // segmento B
        Assert.Equal("2", Field(segB, 18, 1));             // CNPJ favorecido
        Assert.Equal("11222333000181", Field(segB, 19, 14)); // TED favorecido document
        Assert.Equal(new string(' ', 99), Field(segB, 128, 99)); // PIX key field blank for TED
    }

    [Fact]
    public void Pix_lote_header_has_lote_0002_and_forma_45()
    {
        var pixHeader = Generate()[5];

        Assert.Equal("0002", Field(pixHeader, 4, 4)); // second lote
        Assert.Equal("1", Field(pixHeader, 8, 1));
        Assert.Equal("20", Field(pixHeader, 10, 2));
        Assert.Equal("45", Field(pixHeader, 12, 2));  // forma PIX = 45
    }

    [Fact]
    public void Pix_segment_a_amount_and_reference_but_blank_favored_bank()
    {
        var segA = Generate()[6];

        Assert.Equal("0002", Field(segA, 4, 4));
        Assert.Equal("00001", Field(segA, 9, 5));            // seq resets per lote
        Assert.Equal("A", Field(segA, 14, 1));
        Assert.Equal("000", Field(segA, 18, 3));             // camara 000 for PIX
        Assert.Equal("000", Field(segA, 21, 3));             // favored bank zeros
        Assert.Equal("000000000000", Field(segA, 30, 12));   // favored account zeros
        Assert.Equal("00004200002         ", Field(segA, 74, 20)); // Reference
        Assert.Equal("02072026", Field(segA, 94, 8));        // payment date
        Assert.Equal("000000000005000", Field(segA, 120, 15)); // 50.00 → 5000
        Assert.Equal("     ", Field(segA, 220, 5));          // finalidade TED blank for PIX
    }

    [Fact]
    public void Pix_segment_b_carries_pix_key_in_info_12_and_initiation_form()
    {
        var segB = Generate()[7];

        Assert.Equal("00002", Field(segB, 9, 5));
        Assert.Equal("B", Field(segB, 14, 1));
        Assert.Equal("02", Field(segB, 15, 2)); // forma iniciacao = email
        Assert.Equal("2", Field(segB, 18, 1));  // CNPJ favorecido
        // PIX key transported in Informação 12 (128-226), left-justified, uppercased.
        var info12 = Field(segB, 128, 99);
        Assert.StartsWith("BETA@PIX.EXAMPLE.COM", info12);
        Assert.Equal("BETA@PIX.EXAMPLE.COM".PadRight(99), info12);
    }

    [Fact]
    public void Lote_trailer_record_count_and_sum_match_the_lote()
    {
        var r = Generate();
        var tedTrailer = r[4];
        var pixTrailer = r[8];

        Assert.Equal("5", Field(tedTrailer, 8, 1));
        Assert.Equal("0001", Field(tedTrailer, 4, 4));
        Assert.Equal("000004", Field(tedTrailer, 18, 6)); // 2 (hdr+trl) + 2 (segA+segB) = 4
        Assert.Equal("000000000000123456", Field(tedTrailer, 24, 18)); // sum 1234.56

        Assert.Equal("0002", Field(pixTrailer, 4, 4));
        Assert.Equal("000004", Field(pixTrailer, 18, 6));
        Assert.Equal("000000000000005000", Field(pixTrailer, 24, 18)); // sum 50.00
    }

    [Fact]
    public void File_trailer_counts_two_lotes_and_ten_records()
    {
        var trailer = Generate()[9];

        Assert.Equal("9999", Field(trailer, 4, 4));
        Assert.Equal("9", Field(trailer, 8, 1));
        Assert.Equal("000002", Field(trailer, 18, 6)); // 2 lotes
        Assert.Equal("000010", Field(trailer, 24, 6)); // 10 records (0,1,3,3,5,1,3,3,5,9)
    }

    // ----- Convênio agency-segment tests (AEL-613 fix: right-justify agency in pos 33-52) -----

    [Fact]
    public void Convenio_4digit_agency_is_unchanged_in_header_pos33to52()
    {
        // Payer.Agency = "1234" (4 digits) — must produce 0033 + 1234 + 987654321098 (20 chars total).
        var header = Generate()[0];
        var convenio = Field(header, 33, 20); // full 20-char convenio field
        Assert.Equal("00331234987654321098", convenio);
        // Verify the 4-digit agency sits at pos 37-40 (chars 5-8 of the 20-char field).
        Assert.Equal("1234", convenio.Substring(4, 4));
    }

    [Fact]
    public void Convenio_5digit_agency_keeps_rightmost_4_in_header()
    {
        // Agency "12345" (5 digits): rightmost 4 = "2345"; result must NOT be "1234".
        var payer5 = Payer with { Agency = "12345" };
        var input = new RemittanceFileInput(
            BankCode: "033",
            SequenceNumber: 42,
            GeneratedAt: GeneratedAt,
            Payer: payer5,
            Lines: new[] { TedLine });

        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        var bytes = writer.Generate(input);
        var header = Encoding.Latin1.GetString(bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0];

        var convenio = Field(header, 33, 20);
        Assert.Equal("2345", convenio.Substring(4, 4)); // rightmost 4 of "12345"
        Assert.NotEqual("1234", convenio.Substring(4, 4)); // must NOT silently truncate leftmost 4
    }
}
