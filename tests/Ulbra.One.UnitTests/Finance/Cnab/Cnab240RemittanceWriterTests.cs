using System.Text;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Infrastructure.Cnab;
using Xunit;

namespace Ulbra.One.UnitTests.Finance.Cnab;

/// <summary>
/// Golden-file test for the Santander 033 CNAB240 writer. A fixed input (1 TED + 1 PIX line →
/// 2 lotes) is generated, split on CRLF, and asserted position-by-position against the
/// layout in <c>docs/cnab240-layout.md</c>. Positions below are 1-based (as in
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
        Reference: "00004200001",
        DueDate: new DateOnly(2026, 6, 28),
        FavoredAddressStreet: "Rua das Flores",
        FavoredAddressNumber: "100",
        FavoredAddressComplement: "Sala 5",
        FavoredAddressNeighborhood: "Centro",
        FavoredAddressCity: "Porto Alegre",
        FavoredAddressCep: "90000000",
        FavoredAddressState: "RS");

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
    public void AgencyDigit_of_two_chars_overflows_the_one_char_header_field()
    {
        // Repro (remittance 500): BankAccount.AgencyDigit is varchar(2) in the DB, but the CNAB
        // file/batch header dv_agencia field (pos 58) is a single char written non-truncating. A
        // stored 2-char agency check digit therefore throws deep in the writer, which surfaces as
        // an unhandled HTTP 500 on generation. CnabFieldValidation.PayerReason validates AccountDigit
        // (≤1) but NOT AgencyDigit — the asymmetry that lets bad payer data reach the writer.
        var input = BuildInput() with { Payer = Payer with { AgencyDigit = "12" } };
        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());

        var ex = Assert.Throws<InvalidOperationException>(() => writer.Generate(input));
        Assert.Contains("at 58", ex.Message);
    }

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
        Assert.Equal("206", Field(header, 164, 3));           // versao layout — confirmado contra remessa aceita
        Assert.Equal("     ", Field(header, 167, 5));          // densidade — em branco na remessa aceita
        Assert.Equal("BANCO SANTANDER", Field(header, 103, 15)); // nome banco
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
        Assert.Equal(" ", Field(segA, 29, 1));                 // DV agência favorecido = Branco (manual)
        Assert.Equal("000012345678", Field(segA, 30, 12));     // favored account
        Assert.Equal("9", Field(segA, 42, 1));                 // favored account DV
        Assert.Equal("FORNECEDOR ALPHA LTDA         ", Field(segA, 44, 30)); // name uppercased, padded
        Assert.Equal("00004200001         ", Field(segA, 74, 20)); // "Seu Numero" = Reference untruncated, left-justified
        Assert.Equal("01072026", Field(segA, 94, 8));          // payment date
        Assert.Equal("BRL", Field(segA, 102, 3));              // moeda
        Assert.Equal("000000000123456", Field(segA, 120, 15)); // valor 1234.56 → 123456
        Assert.Equal("00005", Field(segA, 220, 5));            // finalidade TED = 00005 fornecedores
    }

    [Fact]
    public void Ted_segment_b_carries_favored_inscription_address_due_date_and_value()
    {
        var segB = Generate()[3];

        Assert.Equal("00002", Field(segB, 9, 5));          // seq within lote = 2
        Assert.Equal("B", Field(segB, 14, 1));             // segmento B
        Assert.Equal("00", Field(segB, 15, 2));            // forma iniciacao = 0 for non-PIX
        Assert.Equal("2", Field(segB, 18, 1));             // CNPJ favorecido
        Assert.Equal("11222333000181", Field(segB, 19, 14)); // TED favorecido document
        // Enriched non-PIX layout: favored address + UF + vencimento + valor.
        Assert.Equal("RUA DAS FLORES".PadRight(30), Field(segB, 33, 30)); // logradouro uppercased
        Assert.Equal("00100", Field(segB, 63, 5));         // número
        Assert.Equal("SALA 5".PadRight(15), Field(segB, 68, 15));   // complemento
        Assert.Equal("CENTRO".PadRight(15), Field(segB, 83, 15));   // bairro
        Assert.Equal("PORTO ALEGRE".PadRight(20), Field(segB, 98, 20)); // cidade
        Assert.Equal("90000000", Field(segB, 118, 8));     // CEP
        Assert.Equal("RS", Field(segB, 126, 2));           // UF
        Assert.Equal("28062026", Field(segB, 128, 8));     // vencimento (DueDate)
        Assert.Equal("000000000123456", Field(segB, 136, 15)); // valor nominal 1234.56
    }

    [Fact]
    public void Over_long_pix_key_throws_instead_of_silently_truncating()
    {
        // Hardening (AEL-963): the Seg B key field (128-226, X099) is written NON-truncating. A key longer
        // than 99 — which CnabFieldValidation.FavoredReason skips upstream — must, if it ever reached the
        // writer, throw loudly rather than emit a truncated (corrupt) key that misroutes money.
        var input = BuildInput() with { Lines = new[] { PixLine with { FavoredPixKey = new string('a', 100) } } };
        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());

        var ex = Assert.Throws<InvalidOperationException>(() => writer.Generate(input));
        Assert.Contains("at 128", ex.Message);
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
    public void Pix_segment_a_amount_and_reference_with_favored_bank_033()
    {
        var segA = Generate()[6];

        Assert.Equal("0002", Field(segA, 4, 4));
        Assert.Equal("00001", Field(segA, 9, 5));            // seq resets per lote
        Assert.Equal("A", Field(segA, 14, 1));
        Assert.Equal("009", Field(segA, 18, 3));             // camara 009 = Pix (Nota G014)
        Assert.Equal("033", Field(segA, 21, 3));             // banco favorecido — Obrigatório (manual); Pix usa 033
        Assert.Equal("000000000000", Field(segA, 30, 12));   // favored account zeros (resolvido pela chave)
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
        // PIX key transported in Informação 12 (128-226), left-justified, LOWER-cased (case-sensitive key).
        var info12 = Field(segB, 128, 99);
        Assert.StartsWith("beta@pix.example.com", info12);
        Assert.Equal("beta@pix.example.com".PadRight(99), info12);
    }

    [Fact]
    public void Pix_key_is_lowercased_not_uppercased()
    {
        // PIX keys (e-mail, EVP/UUID) are case-sensitive — the DICT normalizes to lower-case. The writer
        // must lower-case the key (not the CNAB-default upper-case), else EVP/e-mail keys fail to resolve.
        var evpKey = "71D3E9F2-AB4C-4D5E-9F01-2A3B4C5D6E7F"; // EVP com hex maiúsculo
        var input = new RemittanceFileInput(
            BankCode: "033",
            SequenceNumber: 42,
            GeneratedAt: GeneratedAt,
            Payer: Payer,
            Lines: new[] { PixLine with { FavoredPixKey = evpKey } });

        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        var records = Encoding.Latin1.GetString(writer.Generate(input))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        // [0] hdr arquivo · [1] hdr lote · [2] Seg A · [3] Seg B · [4] trl lote · [5] trl arquivo
        var info12 = Field(records[3], 128, 99);
        Assert.Equal(evpKey.ToLowerInvariant().PadRight(99), info12);
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

    [Fact]
    public void Over_long_address_number_and_cep_degrade_gracefully_instead_of_aborting_the_file()
    {
        // Regression guard: endereco número/CEP are informational (Seg B / batch header) and don't
        // gate the transfer. Malformed (too-long) values from vendor/account data must be capped to
        // the field width — never throw an overflow that aborts the whole remittance (HTTP 500).
        var payer = Payer with
        {
            AddressStreet = "Av Principal",
            AddressNumber = "123456",   // 6 digits — overflows the 5-wide payer número field
            AddressCity = "Canoas",
            AddressCep = "999999999",   // 9 digits — overflows the 8-wide CEP (5+3)
            AddressState = "RS",
        };
        var ted = TedLine with
        {
            FavoredAddressNumber = "987654",  // 6 digits — overflows the 5-wide favored número
            FavoredAddressCep = "123456789",  // 9 digits — overflows the 8-wide favored CEP
        };

        var input = new RemittanceFileInput(
            BankCode: "033",
            SequenceNumber: 42,
            GeneratedAt: GeneratedAt,
            Payer: payer,
            Lines: new[] { ted });

        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        var records = Encoding.Latin1.GetString(writer.Generate(input))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var record in records)
            Assert.Equal(240, record.Length); // did not throw; every record still well-formed

        // [0] header arquivo · [1] header lote · [2] Seg A · [3] Seg B · [4] trailer lote · [5] trailer arquivo
        var batchHeader = records[1];
        var segB = records[3];

        // Payer address (batch header): leading digits kept, tail dropped.
        Assert.Equal("12345", Field(batchHeader, 173, 5)); // número (first 5 of "123456")
        Assert.Equal("99999", Field(batchHeader, 213, 5)); // CEP  (first 5 of "999999999")
        Assert.Equal("999", Field(batchHeader, 218, 3));   // CEP complemento (next 3)

        // Favored address (Seg B non-PIX): same defensive capping.
        Assert.Equal("98765", Field(segB, 63, 5));         // número (first 5 of "987654")
        Assert.Equal("12345678", Field(segB, 118, 8));     // CEP (first 8 of "123456789")
    }

    // ----- PIX QR Code dinâmico (copia e cola) — Forma 47 / Seg J-PIX + J52-PIX (AEL-963) -----

    // A dynamic-QR line carries the extracted payload-location URL (never the raw copia-e-cola). Presence of
    // PixQrDynamicUrl routes it to the PixQrCode lote (forma 47) instead of the key-PIX lote (forma 45).
    private static readonly RemittanceFileLine PixQrLine = new(
        Method: PaymentMethod.Pix,
        Amount: 75.00m,
        PaymentDate: new DateOnly(2026, 7, 3),
        FavoredName: "Recebedor QR ME",
        FavoredDocument: "55666777000122",
        FavoredBankCode: "",
        FavoredAgency: "",
        FavoredAccount: "",
        FavoredAccountDigit: "",
        FavoredPixKey: "",
        Reference: "00004200003",
        PixQrDynamicUrl: "pix.example.com/qr/v2/abc123def456",
        PixQrReceiverName: "FULANO TESTE");

    private static string[] GeneratePixQr(RemittanceFileLine line)
    {
        var input = new RemittanceFileInput(
            BankCode: "033", SequenceNumber: 42, GeneratedAt: GeneratedAt, Payer: Payer, Lines: new[] { line });
        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        return Encoding.Latin1.GetString(writer.Generate(input))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Pix_qr_dynamic_lote_header_has_forma_47()
    {
        // [0] hdr arquivo · [1] hdr lote · [2] Seg J-PIX · [3] Seg J52-PIX · [4] trl lote · [5] trl arquivo
        var records = GeneratePixQr(PixQrLine);
        Assert.Equal(6, records.Length);
        var loteHeader = records[1];
        Assert.Equal("20", Field(loteHeader, 10, 2)); // serviço fornecedores
        Assert.Equal("47", Field(loteHeader, 12, 2)); // forma PIX QR Code = 47
    }

    [Fact]
    public void Pix_qr_segment_j_carries_amount_reference_and_blank_txid()
    {
        var segJ = GeneratePixQr(PixQrLine)[2];
        Assert.Equal("J", Field(segJ, 14, 1));                     // segmento J
        Assert.Equal("00001", Field(segJ, 9, 5));                 // seq within lote
        Assert.Equal(new string(' ', 44), Field(segJ, 18, 44));  // TXID blank on remessa (G036)
        Assert.Equal("000000000007500", Field(segJ, 153, 15));   // valor pagamento 75.00
        Assert.Equal("00004200003         ", Field(segJ, 183, 20)); // "Seu Número" (Reference), left-justified
        Assert.Equal("00", Field(segJ, 223, 2));                 // código moeda zeros (não 09 como no boleto)
    }

    [Fact]
    public void Pix_qr_segment_j52_carries_payer_url_and_zero_receiver_doc()
    {
        var segJ52 = GeneratePixQr(PixQrLine)[3];
        Assert.Equal("52", Field(segJ52, 18, 2));                 // identificação registro 52
        Assert.Equal("2", Field(segJ52, 20, 1));                 // tipo inscrição pagador = CNPJ
        Assert.Equal("092814703000150", Field(segJ52, 21, 15));  // pagador CNPJ (14 díg right-just em 15)
        Assert.Equal("0", Field(segJ52, 76, 1));                 // recebedor tipo inscrição = 0 (não valida titularidade)
        Assert.Equal(new string('0', 15), Field(segJ52, 77, 15)); // recebedor CPF/CNPJ zeros
        // URL de endereçamento (G041) em 132-210, case preservado, left-justified.
        var url = Field(segJ52, 132, 79);
        Assert.StartsWith("pix.example.com/qr/v2/abc123def456", url);
        Assert.Equal("pix.example.com/qr/v2/abc123def456".PadRight(79), url);
    }

    [Fact]
    public void Pix_qr_lote_trailer_counts_four_records()
    {
        var trailer = GeneratePixQr(PixQrLine)[4];
        Assert.Equal("5", Field(trailer, 8, 1));
        Assert.Equal("000004", Field(trailer, 18, 6)); // hdr + J-PIX + J52-PIX + trl
        Assert.Equal("000000000000007500", Field(trailer, 24, 18)); // sum 75.00
    }

    [Fact]
    public void Plain_pix_and_qr_land_in_separate_lotes_forma_45_and_47()
    {
        // A plain-key PIX line (forma 45, Seg A+B) and a dynamic-QR line (forma 47, Seg J+J52) must never
        // share a lote — each Header de Lote carries one forma.
        var input = new RemittanceFileInput(
            BankCode: "033", SequenceNumber: 42, GeneratedAt: GeneratedAt, Payer: Payer,
            Lines: new[] { PixLine, PixQrLine });
        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        var records = Encoding.Latin1.GetString(writer.Generate(input))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // 0 + [1 A B 5] (forma 45) + [1 J J52 5] (forma 47) + 9 = 10 records.
        Assert.Equal(10, records.Length);
        Assert.Equal("45", Field(records[1], 12, 2)); // first lote forma 45 (key PIX)
        Assert.Equal("47", Field(records[5], 12, 2)); // second lote forma 47 (QR)
    }
}
