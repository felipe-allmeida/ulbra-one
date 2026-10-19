using System.Text;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Cnab;
using Ulbra.One.Finance.Infrastructure.Cnab;
using Xunit;

namespace Ulbra.One.UnitTests.Finance.Cnab;

/// <summary>
/// Writer tests for boleto bancário (Segmento J + J-52) and convênio/tributo (Segmento O), plus
/// the hybrid-lote separation. Independent input from the TED+PIX golden so it stays untouched.
/// Positions 1-based per <c>docs/cnab240-layout.md</c>. 🔶 boleto/convênio layout awaits homologação.
/// </summary>
public class Cnab240BoletoWriterTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly RemittancePayerInfo Payer = new(
        Cnpj: "92814703000150",
        LegalName: "ULBRA EDUCACAO",
        Agency: "1234",
        AccountNumber: "000000567890",
        AccountDigit: "1",
        AgreementCode: "987654321098");

    // 47-digit boleto bancário linha digitável (labeled segments — see BoletoBarcodeTests).
    private const string BancarioLinha =
        "0339" + "11111" + "2" + "3333333333" + "4" + "5555555555" + "6" + "7" + "8888" + "9999999999";

    // 48-digit convênio/arrecadação linha digitável (starts with 8).
    private const string ConvenioLinha =
        "81111111111" + "2" + "33333333333" + "4" + "55555555555" + "6" + "77777777777" + "8";

    private static readonly RemittanceFileLine BoletoLine = new(
        Method: PaymentMethod.Boleto,
        Amount: 500.00m,
        PaymentDate: new DateOnly(2026, 7, 10),
        FavoredName: "Cedente Boleto LTDA",
        FavoredDocument: "11222333000181",
        FavoredBankCode: "",
        FavoredAgency: "",
        FavoredAccount: "",
        FavoredAccountDigit: "",
        FavoredPixKey: "",
        Reference: "00007700001",
        BoletoDigitableLine: BancarioLinha,
        DueDate: new DateOnly(2026, 7, 5));

    private static readonly RemittanceFileLine ConvenioLine = new(
        Method: PaymentMethod.Boleto,
        Amount: 89.90m,
        PaymentDate: new DateOnly(2026, 7, 11),
        FavoredName: "Concessionaria Agua SA",
        FavoredDocument: "55666777000122",
        FavoredBankCode: "",
        FavoredAgency: "",
        FavoredAccount: "",
        FavoredAccountDigit: "",
        FavoredPixKey: "",
        Reference: "00007700002",
        BoletoDigitableLine: ConvenioLinha,
        DueDate: new DateOnly(2026, 7, 6));

    private static string[] Generate(params RemittanceFileLine[] lines)
    {
        var input = new RemittanceFileInput("033", 77, GeneratedAt, Payer, lines);
        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        var text = Encoding.Latin1.GetString(writer.Generate(input));
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Field(string record, int start, int length) => record.Substring(start - 1, length);

    [Fact]
    public void Boleto_bancario_emits_header_J_J52_and_trailer()
    {
        var r = Generate(BoletoLine);

        // 0 (file header) + 1 (lote) + J + J-52 + 5 (lote trailer) + 9 (file trailer) = 6 records.
        Assert.Equal(6, r.Length);
        foreach (var record in r)
            Assert.Equal(240, record.Length);

        var lote = r[1];
        Assert.Equal("0001", Field(lote, 4, 4));
        Assert.Equal("20", Field(lote, 10, 2));  // tipo servico fornecedores
        Assert.Equal("31", Field(lote, 12, 2));  // forma boleto
        Assert.Equal("030", Field(lote, 14, 3)); // versao lote boleto
    }

    [Fact]
    public void Segment_J_carries_barcode_names_dates_value_and_reference()
    {
        var segJ = Generate(BoletoLine)[2];

        Assert.Equal("3", Field(segJ, 8, 1));
        Assert.Equal("00001", Field(segJ, 9, 5));
        Assert.Equal("J", Field(segJ, 14, 1));
        Assert.Equal(BoletoBarcode.BancarioTo44(BancarioLinha), Field(segJ, 18, 44));
        Assert.Equal("CEDENTE BOLETO LTDA           ", Field(segJ, 62, 30)); // uppercased, padded
        Assert.Equal("05072026", Field(segJ, 92, 8));            // vencimento
        Assert.Equal("000000000050000", Field(segJ, 100, 15));   // valor nominal 500.00
        Assert.Equal("10072026", Field(segJ, 145, 8));           // data pagamento
        Assert.Equal("000000000050000", Field(segJ, 153, 15));   // valor pagamento
        Assert.Equal("00007700001         ", Field(segJ, 183, 20)); // Seu Número untruncated
        Assert.Equal("09", Field(segJ, 223, 2));                 // moeda
    }

    [Fact]
    public void Segment_J52_carries_pagador_payer_and_beneficiario_favored()
    {
        // Layout manual Santander v11.7 §3.4: cód. movimento (16-17), "52" (18-19),
        // pagador (20-75), beneficiário (76-131), sacador (132-187).
        var segJ52 = Generate(BoletoLine)[3];

        Assert.Equal("00002", Field(segJ52, 9, 5)); // second detail record in the lote
        Assert.Equal("J", Field(segJ52, 14, 1));
        Assert.Equal("00", Field(segJ52, 16, 2));   // código de movimento remessa (G026 = zeros)
        Assert.Equal("52", Field(segJ52, 18, 2));   // identificação registro = 52
        Assert.Equal("2", Field(segJ52, 20, 1));    // tipo inscrição pagador (CNPJ)
        Assert.Equal("092814703000150", Field(segJ52, 21, 15)); // pagador doc = payer cnpj (15)
        Assert.Equal("ULBRA EDUCACAO", Field(segJ52, 36, 14));  // pagador name (start)
        Assert.Equal("2", Field(segJ52, 76, 1));    // tipo inscrição beneficiário (CNPJ)
        Assert.Equal("011222333000181", Field(segJ52, 77, 15)); // beneficiário doc (15)
        Assert.Equal("CEDENTE BOLETO LTDA", Field(segJ52, 92, 19)); // beneficiário name (start)
    }

    [Fact]
    public void Payer_cnpj_and_legal_name_reach_file_header_lote_header_and_j52()
    {
        // A distinct payer identity (e.g. an account overriding its company's CNPJ) must land in every
        // payer slot the writer produces: Header de Arquivo (19/73), Header de Lote (19/73) and J-52 (21/36).
        var payer = Payer with { Cnpj = "11444777000161", LegalName = "ESTABELECIMENTO PAGADOR LTDA" };
        var input = new RemittanceFileInput("033", 77, GeneratedAt, payer, new[] { BoletoLine });
        var writer = new Cnab240RemittanceWriter(new SantanderCnabProfile());
        var r = Encoding.Latin1.GetString(writer.Generate(input)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // r[0] Header de Arquivo · r[1] Header de Lote · r[2] Seg J · r[3] Seg J-52.
        Assert.Equal("11444777000161", Field(r[0], 19, 14));                          // file header CNPJ
        Assert.Equal("ESTABELECIMENTO PAGADOR LTDA".PadRight(30), Field(r[0], 73, 30)); // file header razão social
        Assert.Equal("11444777000161", Field(r[1], 19, 14));                          // lote header CNPJ
        Assert.Equal("ESTABELECIMENTO PAGADOR LTDA".PadRight(30), Field(r[1], 73, 30)); // lote header razão social
        Assert.Equal("011444777000161", Field(r[3], 21, 15));                         // J-52 pagador CNPJ (zero-left-pad to 15)
        Assert.Equal("ESTABELECIMENTO PAGADOR LTDA".PadRight(40), Field(r[3], 36, 40)); // J-52 pagador razão social
    }

    [Fact]
    public void Convenio_emits_header_and_single_segment_O()
    {
        var r = Generate(ConvenioLine);

        // 0 + 1 (lote) + O + 5 + 9 = 5 records (single detail per line).
        Assert.Equal(5, r.Length);
        foreach (var record in r)
            Assert.Equal(240, record.Length);

        var lote = r[1];
        Assert.Equal("22", Field(lote, 10, 2));  // tipo servico tributos
        Assert.Equal("11", Field(lote, 12, 2));  // forma convenio
        Assert.Equal("010", Field(lote, 14, 3)); // versao lote convenio

        var segO = r[2];
        Assert.Equal("O", Field(segO, 14, 1));
        var barcode = Field(segO, 18, 44);
        Assert.Equal(BoletoBarcode.ConvenioTo44(ConvenioLinha), barcode);
        Assert.StartsWith("8", barcode);
        Assert.Equal("06072026", Field(segO, 92, 8));          // vencimento
        Assert.Equal("11072026", Field(segO, 100, 8));         // data pagamento
        Assert.Equal("000000000008990", Field(segO, 108, 15)); // valor 89.90
        Assert.Equal("00007700002         ", Field(segO, 123, 20)); // Seu Número

        // Lote trailer: header + O + trailer = 3 records.
        Assert.Equal("000003", Field(r[3], 18, 6));
    }

    [Fact]
    public void Boleto_bancario_and_convenio_never_share_a_lote()
    {
        var r = Generate(BoletoLine, ConvenioLine);

        // 0 + [1 + J + J52 + 5] (boleto) + [1 + O + 5] (convênio) + 9 = 9 records.
        Assert.Equal(9, r.Length);

        var boletoHeader = r[1];
        Assert.Equal("0001", Field(boletoHeader, 4, 4));
        Assert.Equal("31", Field(boletoHeader, 12, 2)); // boleto lote

        var convenioHeader = r[5];
        Assert.Equal("0002", Field(convenioHeader, 4, 4));
        Assert.Equal("11", Field(convenioHeader, 12, 2)); // convênio lote

        // File trailer: 2 lotes, 9 records.
        Assert.Equal("000002", Field(r[8], 18, 6));
        Assert.Equal("000009", Field(r[8], 24, 6));
    }
}
