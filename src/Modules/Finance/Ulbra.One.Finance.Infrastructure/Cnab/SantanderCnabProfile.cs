using System.Globalization;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Cnab;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// Banco Santander (033) "Pagamento a Fornecedores" CNAB240 layout — YLEC_2403 v11.3.1,
/// over the FEBRABAN v10.11 base. Field positions/lengths are encoded inline below; every
/// record is verified to cover all 240 bytes via <see cref="CnabRecordBuilder.BuildComplete"/>.
///
/// See <c>docs/cnab240-layout.md</c> for the authoritative field map and the
/// homologação flags (TED forma de lançamento code, PIX Segmento B sub-boundaries).
/// </summary>
public sealed class SantanderCnabProfile : ICnabBankProfile
{
    public string BankCode => "033";
    // Header nome_banco (103-132). The reference generator (bank-accepted remessa) uses "BANCO SANTANDER".
    public string BankName => "BANCO SANTANDER";

    /// <summary>Tipo de Serviço (Header de Lote 10-11): 20 = Pagamento a Fornecedores.</summary>
    private const string TipoServicoFornecedores = "20";

    /// <summary>Forma de Lançamento — crédito em conta (not used: our PaymentMethod resolves to Pix/Ted only).</summary>
    private const string FormaCreditoEmConta = "01";

    /// <summary>
    /// Forma de Lançamento — TED. YLEC manual v11.7 G002: "03" = Transferências para outros bancos
    /// (TED CIP e TED STR). Câmara 018 (CIP) / 810 (STR) / 888 per Nota G014.
    /// </summary>
    public const string FormaTed = "03";

    /// <summary>Forma de Lançamento — PIX Transferência.</summary>
    public const string FormaPix = "45";

    /// <summary>
    /// Versão do layout do arquivo (Header de Arquivo 164-166). The YLEC manual v11.7 specifies "060", but
    /// the two bank-accepted legacy remessas both use "206"; by decision we mirror the proven-accepted files
    /// (the bank does not validate this field strictly). Flip back to "060" to follow the manual literally.
    /// </summary>
    private const string FileLayoutVersion = "206";

    /// <summary>Versão do layout do lote (Header de Lote 14-16) — Santander fixed value (PIX/TED).</summary>
    private const string BatchLayoutVersion = "031";

    /// <summary>Tipo de Serviço (Header de Lote 10-11): 22 = Pagamento de Tributos/Convênios.</summary>
    private const string TipoServicoTributos = "22";

    /// <summary>
    /// Forma de Lançamento — boleto bancário (Segmento J). YLEC manual v11.7 G002: "31" = Liquidação de
    /// títulos outros Bancos.
    /// </summary>
    private const string FormaBoleto = "31";

    /// <summary>
    /// Forma de Lançamento — convênio/tributo (Segmento O). "11" confirmed against a real accepted remessa.
    /// </summary>
    private const string FormaConvenio = "11";

    /// <summary>Versão do layout do lote — boleto bancário. 🔶 reference candidate; confirm in YLEC.</summary>
    private const string BatchLayoutVersionBoleto = "030";

    /// <summary>Versão do layout do lote — convênio/tributo (Header de Lote 14-16). "010" confirmed against a real accepted remessa.</summary>
    private const string BatchLayoutVersionConvenio = "010";

    /// <summary>Código da Moeda (Segmento J 223-224): 09 = Real.</summary>
    private const string MoedaReal = "09";

    /// <summary>Câmara centralizadora (Segmento A 18-20, Nota G014): 018 = TED CIP. (810 = TED STR.)</summary>
    private const string CamaraTed = "018";

    /// <summary>Câmara centralizadora (Segmento A 18-20, Nota G014): 009 = Pix.</summary>
    private const string CamaraPix = "009";

    /// <summary>Finalidade (Segmento A 220-224, Nota G013 A): 00005 = Pagamento a Fornecedores.</summary>
    private const string FinalidadeFornecedores = "00005";

    private static readonly IReadOnlyList<CnabSegment> SegmentsAB = new[] { CnabSegment.A, CnabSegment.B };
    private static readonly IReadOnlyList<CnabSegment> SegmentsJ = new[] { CnabSegment.J, CnabSegment.J52 };
    private static readonly IReadOnlyList<CnabSegment> SegmentsO = new[] { CnabSegment.O };

    public CnabLoteCategory CategoryFor(RemittanceFileLine line) => line.Method switch
    {
        PaymentMethod.Pix => CnabLoteCategory.Pix,
        PaymentMethod.Ted => CnabLoteCategory.Ted,
        // Boleto splits by código de barras: arrecadação/convênio starts with '8' → Segmento O.
        PaymentMethod.Boleto => BoletoBarcode.IsConvenio(line.BoletoDigitableLine)
            ? CnabLoteCategory.Convenio
            : CnabLoteCategory.BoletoBancario,
        _ => throw new NotSupportedException(
            $"Payment method '{line.Method}' is not supported for CNAB240 remittance.")
    };

    public IReadOnlyList<CnabSegment> DetailSegmentsFor(CnabLoteCategory category) => category switch
    {
        CnabLoteCategory.Pix or CnabLoteCategory.Ted => SegmentsAB,
        CnabLoteCategory.BoletoBancario => SegmentsJ,
        CnabLoteCategory.Convenio => SegmentsO,
        _ => throw new NotSupportedException($"Lote category '{category}' is not supported by CNAB240.")
    };

    public string BuildDetailSegment(
        RemittanceFileInput input,
        CnabBatchContext batch,
        RemittanceFileLine line,
        CnabSegment segment,
        int sequenceInBatch) => segment switch
    {
        CnabSegment.A => BuildSegmentA(input, batch, line, sequenceInBatch),
        CnabSegment.B => BuildSegmentB(input, batch, line, sequenceInBatch),
        CnabSegment.J => BuildSegmentJ(batch, line, sequenceInBatch),
        CnabSegment.J52 => BuildSegmentJ52(input, batch, line, sequenceInBatch),
        CnabSegment.O => BuildSegmentO(batch, line, sequenceInBatch),
        _ => throw new NotSupportedException($"Segment '{segment}' is not supported by CNAB240."),
    };

    /// <summary>Tipo de Serviço (Header de Lote 10-11) for a lote category.</summary>
    private static string TipoServicoFor(CnabLoteCategory category) =>
        category == CnabLoteCategory.Convenio ? TipoServicoTributos : TipoServicoFornecedores;

    /// <summary>Forma de Lançamento (Header de Lote 12-13) for a lote category.</summary>
    private static string FormaFor(CnabLoteCategory category) => category switch
    {
        CnabLoteCategory.Pix => FormaPix,
        CnabLoteCategory.Ted => FormaTed,
        CnabLoteCategory.BoletoBancario => FormaBoleto,
        CnabLoteCategory.Convenio => FormaConvenio,
        _ => throw new NotSupportedException($"Lote category '{category}' has no forma de lançamento.")
    };

    /// <summary>Versão do layout do lote (Header de Lote 14-16) for a lote category.</summary>
    private static string BatchVersionFor(CnabLoteCategory category) => category switch
    {
        CnabLoteCategory.BoletoBancario => BatchLayoutVersionBoleto,
        CnabLoteCategory.Convenio => BatchLayoutVersionConvenio,
        _ => BatchLayoutVersion
    };

    // ---- Record 0: Header de Arquivo --------------------------------------------------------

    public string BuildFileHeader(RemittanceFileInput input)
    {
        var p = input.Payer;
        var b = new CnabRecordBuilder(240);

        b.Numeric(1, 3, BankCode);                       // codigo_banco
        b.Numeric(4, 4, "0");                            // lote_servico = 0000 (file header)
        b.Numeric(8, 1, "0");                            // tipo_registro = 0
        b.Blank(9, 9);                                   // filler FEBRABAN
        b.Numeric(18, 1, "2");                           // tipo_inscricao = 2 (CNPJ)
        b.Numeric(19, 14, p.Cnpj);                       // numero_inscricao
        b.Alpha(33, 20, AgreementField(p));              // codigo_convenio (BBBBAAAACCCCCCCCCCCC)
        b.Numeric(53, 5, p.Agency);                      // agencia_mantenedora
        b.Alpha(58, 1, p.AgencyDigit);                   // dv_agencia (empty when not informed)
        b.Numeric(59, 12, p.AccountNumber);              // numero_conta_corrente
        b.Alpha(71, 1, p.AccountDigit);                  // dv_conta
        b.Blank(72, 1);                                  // dv_agencia_conta (Santander: blank)
        b.Alpha(73, 30, p.LegalName, allowTruncate: true); // nome_empresa
        b.Alpha(103, 30, BankName, allowTruncate: true); // nome_banco
        b.Blank(133, 10);                                // filler FEBRABAN
        b.Numeric(143, 1, "1");                          // codigo_remessa = 1 (remessa)
        b.Date(144, 8, DateOnly.FromDateTime(input.GeneratedAt.Date)); // data_geracao
        b.Numeric(152, 6, input.GeneratedAt.ToString("HHmmss", CultureInfo.InvariantCulture)); // hora_geracao
        b.Numeric(158, 6, input.SequenceNumber);         // numero_sequencial_arquivo (NSA)
        b.Numeric(164, 3, FileLayoutVersion);            // versao_layout_arquivo
        b.Blank(167, 5);                                 // densidade_gravacao — accepted remessa leaves it blank
        b.Blank(172, 20);                                // uso_reservado_banco
        b.Blank(192, 20);                                // uso_reservado_empresa
        b.Blank(212, 19);                                // filler FEBRABAN
        b.Blank(231, 10);                                // ocorrencias_retorno (remessa: blank)

        return b.BuildComplete();
    }

    // ---- Record 1: Header de Lote -----------------------------------------------------------

    public string BuildBatchHeader(RemittanceFileInput input, CnabBatchContext batch)
    {
        var p = input.Payer;
        var b = new CnabRecordBuilder(240);

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, batch.LoteNumber);                // lote_servico
        b.Numeric(8, 1, "1");                             // tipo_registro = 1
        b.Alpha(9, 1, "C");                               // tipo_operacao = C (crédito)
        b.Numeric(10, 2, TipoServicoFor(batch.Category)); // tipo_servico (20 fornecedores / 22 tributos)
        b.Numeric(12, 2, FormaFor(batch.Category));       // forma_lancamento
        b.Numeric(14, 3, BatchVersionFor(batch.Category));// versao_layout_lote
        b.Blank(17, 1);                                   // filler FEBRABAN
        b.Numeric(18, 1, "2");                            // tipo_inscricao_empresa = 2 (CNPJ)
        b.Numeric(19, 14, p.Cnpj);                        // numero_inscricao_empresa
        b.Alpha(33, 20, AgreementField(p));               // codigo_convenio
        b.Numeric(53, 5, p.Agency);                       // agencia_mantenedora
        b.Alpha(58, 1, p.AgencyDigit);                    // dv_agencia (empty when not informed)
        b.Numeric(59, 12, p.AccountNumber);               // numero_conta_corrente
        b.Alpha(71, 1, p.AccountDigit);                   // dv_conta
        b.Blank(72, 1);                                   // dv_agencia_conta
        b.Alpha(73, 30, p.LegalName, allowTruncate: true);// nome_empresa
        b.Blank(103, 40);                                 // informacao_1 (mensagem)

        // Payer address (Header de Lote) — blank when not configured on the BankAccount.
        var payerCep = OnlyDigits(p.AddressCep);
        b.Alpha(143, 30, p.AddressStreet, allowTruncate: true);     // endereco_logradouro
        b.Numeric(173, 5, p.AddressNumber, allowTruncate: true);    // endereco_numero (informational — never overflows)
        b.Alpha(178, 15, p.AddressComplement, allowTruncate: true); // endereco_complemento
        b.Alpha(193, 20, p.AddressCity, allowTruncate: true);       // endereco_cidade
        b.Numeric(213, 5, payerCep, allowTruncate: true);           // endereco_cep (first 5 digits)
        b.Numeric(218, 3, payerCep.Length > 5 ? payerCep[5..] : "", allowTruncate: true); // endereco_cep_complemento
        b.Alpha(221, 2, p.AddressState);                  // endereco_uf
        b.Blank(223, 8);                                  // filler FEBRABAN
        b.Blank(231, 10);                                 // ocorrencias_retorno

        return b.BuildComplete();
    }

    // ---- Record 3: Segmento A ---------------------------------------------------------------

    private string BuildSegmentA(RemittanceFileInput input, CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch)
    {
        var isTed = batch.Category == CnabLoteCategory.Ted;
        var b = new CnabRecordBuilder(240);

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, batch.LoteNumber);                // lote_servico
        b.Numeric(8, 1, "3");                             // tipo_registro = 3
        b.Numeric(9, 5, sequenceInBatch);                 // numero_sequencial_registro_lote
        b.Alpha(14, 1, "A");                              // codigo_segmento = A
        b.Numeric(15, 1, "0");                            // tipo_movimento = 0 (inclusão)
        b.Numeric(16, 2, "0");                            // codigo_instrucao_movimento = 00

        // Câmara + favorecido bank/agency/account: filled for TED, blank/zeros for PIX (resolved via Seg B key).
        if (isTed)
        {
            b.Numeric(18, 3, CamaraTed);                  // codigo_camara = 018 (TED CIP)
            b.Numeric(21, 3, line.FavoredBankCode);       // codigo_banco_favorecido
            b.Numeric(24, 5, line.FavoredAgency);         // agencia_favorecido
            b.Blank(29, 1);                               // dv_agencia favorecido = blank (Nota G003/pág.10)
            b.Numeric(30, 12, line.FavoredAccount);       // conta_favorecido
            b.Alpha(42, 1, line.FavoredAccountDigit);     // dv_conta (favorecido)
            b.Blank(43, 1);                               // dv_agencia_conta = blank
        }
        else
        {
            b.Numeric(18, 3, CamaraPix);                  // codigo_camara = 009 (Pix — Nota G014)
            b.Numeric(21, 3, BankCode);                   // codigo_banco_favorecido — Obrigatório (manual p15); Pix por chave usa 033
            b.Numeric(24, 5, "0");                        // agencia_favorecido (zeros — resolvido pela chave Pix)
            b.Blank(29, 1);                               // dv_agencia
            b.Numeric(30, 12, "0");                       // conta_favorecido (zeros — resolvido pela chave Pix)
            b.Blank(42, 1);                               // dv_conta
            b.Blank(43, 1);                               // dv_agencia_conta
        }

        b.Alpha(44, 30, line.FavoredName, allowTruncate: true); // nome_favorecido
        b.AlphaStrict(74, 20, line.Reference);            // "Seu Número" — round-trip id, NEVER truncate
        b.Date(94, 8, line.PaymentDate);                  // data_pagamento
        b.Alpha(102, 3, "BRL");                           // tipo_moeda
        b.Numeric(105, 15, "0");                          // quantidade_moeda (zeros for BRL)
        b.Money(120, 15, line.Amount);                    // valor_pagamento
        b.Blank(135, 20);                                 // "Nosso Número" (bank-assigned on retorno)
        b.Numeric(155, 8, "0");                           // data_real_efetivacao (zeros on remessa)
        b.Numeric(163, 15, "0");                          // valor_real_efetivacao (zeros on remessa)
        b.Blank(178, 40);                                 // outras_informacoes
        b.Blank(218, 2);                                  // complemento_tipo_servico
        b.Alpha(220, 5, isTed ? FinalidadeFornecedores : null); // finalidade TED = 00005 fornecedores (G013 A)
        b.Blank(225, 2);                                  // complemento_finalidade
        b.Blank(227, 3);                                  // exclusivo_febraban
        b.Numeric(230, 1, "0");                           // aviso_favorecido = 0
        b.Blank(231, 10);                                 // ocorrencias_retorno

        return b.BuildComplete();
    }

    // ---- Record 3: Segmento B ---------------------------------------------------------------

    private string BuildSegmentB(RemittanceFileInput input, CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch)
    {
        var isPix = batch.Category == CnabLoteCategory.Pix;
        var b = new CnabRecordBuilder(240);

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, batch.LoteNumber);                // lote_servico
        b.Numeric(8, 1, "3");                             // tipo_registro = 3
        b.Numeric(9, 5, sequenceInBatch);                 // numero_sequencial_registro_lote
        b.Alpha(14, 1, "B");                              // codigo_segmento = B
        b.Numeric(15, 2, isPix ? PixInitiationForm(line) : "0"); // forma_iniciacao (PIX key type)
        b.Blank(17, 1);                                   // uso_reservado_banco
        b.Numeric(18, 1, FavoredInscriptionType(line.FavoredDocument)); // tipo_inscricao_favorecido
        b.Numeric(19, 14, line.FavoredDocument);          // numero_inscricao_favorecido

        if (isPix)
        {
            b.Blank(33, 35);                              // Informação 10 (TXID — blank for key transfer)
            b.Blank(68, 60);                              // Informação 11 (mensagem livre)
            b.AlphaLower(128, 99, line.FavoredPixKey, allowTruncate: true); // Informação 12 — CHAVE PIX (128-226), case-sensitive → minúscula
            b.Blank(227, 6);                              // uso_reservado_banco
            b.Blank(233, 8);                              // ISPB (blank/zeros if N/A)
        }
        else
        {
            // Non-PIX (TED / boleto fallback): favored address + vencimento + values.
            // 🔶 sub-boundaries FEBRABAN v10.11; confirm in YLEC + a real file (homologação).
            b.Alpha(33, 30, line.FavoredAddressStreet, allowTruncate: true);       // logradouro
            b.Numeric(63, 5, line.FavoredAddressNumber, allowTruncate: true);      // número (informational — never overflows)
            b.Alpha(68, 15, line.FavoredAddressComplement, allowTruncate: true);   // complemento
            b.Alpha(83, 15, line.FavoredAddressNeighborhood, allowTruncate: true); // bairro
            b.Alpha(98, 20, line.FavoredAddressCity, allowTruncate: true);         // cidade
            b.Numeric(118, 8, line.FavoredAddressCep, allowTruncate: true);        // CEP
            b.Alpha(126, 2, line.FavoredAddressState);                             // UF
            b.Date(128, 8, line.DueDate ?? line.PaymentDate);                      // data de vencimento
            b.Money(136, 15, line.Amount);                                         // valor nominal
            b.Numeric(151, 15, "0");                                               // abatimento
            b.Numeric(166, 15, "0");                                               // descontos
            b.Numeric(181, 15, "0");                                               // juros de mora
            b.Numeric(196, 15, "0");                                               // multa
            b.Blank(211, 2);                                                       // código de identificação
            b.Blank(213, 15);                                                      // valor descritivo
            b.Blank(228, 13);                                                      // reservado
        }

        return b.BuildComplete();
    }

    // ---- Record 3: Segmento J (boleto bancário) ---------------------------------------------
    // 🔶 Base positions FEBRABAN v10.11; confirm sub-boundaries in YLEC + a real file (homologação).

    private string BuildSegmentJ(CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch)
    {
        var b = new CnabRecordBuilder(240);
        var barcode = BoletoBarcode.BancarioTo44(line.BoletoDigitableLine);
        var dueDate = line.DueDate ?? line.PaymentDate;

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, batch.LoteNumber);                // lote_servico
        b.Numeric(8, 1, "3");                             // tipo_registro = 3
        b.Numeric(9, 5, sequenceInBatch);                 // numero_sequencial_registro_lote
        b.Alpha(14, 1, "J");                              // codigo_segmento = J
        b.Numeric(15, 1, "0");                            // tipo_movimento = 0 (inclusão)
        b.Numeric(16, 2, "0");                            // codigo_instrucao_movimento = 00
        b.Numeric(18, 44, barcode);                       // codigo_barras (44)
        b.Alpha(62, 30, line.FavoredName, allowTruncate: true); // nome_cedente/favorecido
        b.Date(92, 8, dueDate);                           // data_vencimento
        b.Money(100, 15, line.Amount);                    // valor_nominal
        b.Numeric(115, 15, "0");                          // desconto + abatimento
        b.Numeric(130, 15, "0");                          // mora + multa
        b.Date(145, 8, line.PaymentDate);                 // data_pagamento
        b.Money(153, 15, line.Amount);                    // valor_pagamento
        b.Numeric(168, 15, "0");                          // uso FEBRABAN
        b.AlphaStrict(183, 20, line.Reference);           // "Seu Número" — NEVER truncate
        b.Blank(203, 20);                                 // "Nosso Número" (bank-assigned)
        b.Numeric(223, 2, MoedaReal);                     // codigo_moeda = 09
        b.Blank(225, 16);                                 // uso FEBRABAN

        return b.BuildComplete();
    }

    // ---- Record 3: Segmento J-52 (sacado / cedente) -----------------------------------------
    // Layout per the Santander Pagamento a Fornecedores manual v11.7 §3.4:
    // 15 blank · 16-17 código de movimento (G026=00) · 18-19 "52" · pagador (20-75) ·
    // beneficiário (76-131) · sacador exempt (132-187). Pagador = paying company.

    private string BuildSegmentJ52(RemittanceFileInput input, CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch)
    {
        var p = input.Payer;
        var b = new CnabRecordBuilder(240);

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, batch.LoteNumber);                // lote_servico
        b.Numeric(8, 1, "3");                             // tipo_registro = 3
        b.Numeric(9, 5, sequenceInBatch);                 // numero_sequencial_registro_lote
        b.Alpha(14, 1, "J");                              // codigo_segmento = J
        b.Blank(15, 1);                                   // filler blank
        b.Numeric(16, 2, "0");                            // codigo_movimento_remessa = 00 (G026: zeros)
        b.Numeric(18, 2, "52");                           // identificacao registro = 52
        // Pagador = paying company
        b.Numeric(20, 1, "2");                            // tipo_inscricao_pagador = 2 (CNPJ)
        b.Numeric(21, 15, p.Cnpj);                        // numero_inscricao_pagador
        b.Alpha(36, 40, p.LegalName, allowTruncate: true);// nome_pagador
        // Beneficiário = favored
        b.Numeric(76, 1, FavoredInscriptionType(line.FavoredDocument)); // tipo_inscricao_beneficiario
        b.Numeric(77, 15, line.FavoredDocument);          // numero_inscricao_beneficiario
        b.Alpha(92, 40, line.FavoredName, allowTruncate: true); // nome_beneficiario
        // Sacador = exempt
        b.Numeric(132, 1, "0");                           // tipo_inscricao_sacador
        b.Numeric(133, 15, "0");                          // numero_inscricao_sacador
        b.Blank(148, 40);                                 // nome_sacador
        b.Blank(188, 53);                                 // filler

        return b.BuildComplete();
    }

    // ---- Record 3: Segmento O (convênio / concessionária / tributo) -------------------------
    // Barcode starts with '8'. Sub-boundaries confirmed byte-for-byte against a real accepted remessa.

    private string BuildSegmentO(CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch)
    {
        var b = new CnabRecordBuilder(240);
        var barcode = BoletoBarcode.ConvenioTo44(line.BoletoDigitableLine);
        var dueDate = line.DueDate ?? line.PaymentDate;

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, batch.LoteNumber);                // lote_servico
        b.Numeric(8, 1, "3");                             // tipo_registro = 3
        b.Numeric(9, 5, sequenceInBatch);                 // numero_sequencial_registro_lote
        b.Alpha(14, 1, "O");                              // codigo_segmento = O
        b.Numeric(15, 1, "0");                            // tipo_movimento = 0
        b.Numeric(16, 2, "0");                            // codigo_instrucao_movimento = 00
        b.Numeric(18, 44, barcode);                       // codigo_barras (44, starts with 8)
        b.Alpha(62, 30, line.FavoredName, allowTruncate: true); // nome_cedente
        b.Date(92, 8, dueDate);                           // data_vencimento
        b.Date(100, 8, line.PaymentDate);                 // data_pagamento
        b.Money(108, 15, line.Amount);                    // valor
        b.AlphaStrict(123, 20, line.Reference);           // "Seu Número" / control id — NEVER truncate
        b.Blank(143, 98);                                 // blanks

        return b.BuildComplete();
    }

    // ---- Record 5: Trailer de Lote ----------------------------------------------------------

    public string BuildBatchTrailer(CnabBatchContext batch, int recordCountInLote)
    {
        var b = new CnabRecordBuilder(240);

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, batch.LoteNumber);                // lote_servico
        b.Numeric(8, 1, "5");                             // tipo_registro = 5
        b.Blank(9, 9);                                    // filler FEBRABAN
        b.Numeric(18, 6, recordCountInLote);              // qtd registros do lote (header+detalhes+trailer)
        b.Money(24, 18, batch.TotalAmount);               // somatoria dos valores
        b.Numeric(42, 18, "0");                           // somatoria qtd moedas (zeros for BRL)
        b.Numeric(60, 6, "0");                            // numero aviso debito (zeros on remessa)
        b.Blank(66, 165);                                 // filler FEBRABAN
        b.Blank(231, 10);                                 // ocorrencias_retorno

        return b.BuildComplete();
    }

    // ---- Record 9: Trailer de Arquivo -------------------------------------------------------

    public string BuildFileTrailer(RemittanceFileInput input, CnabFileTotals totals)
    {
        var b = new CnabRecordBuilder(240);

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, "9999");                          // lote_servico = 9999
        b.Numeric(8, 1, "9");                             // tipo_registro = 9
        b.Blank(9, 9);                                    // filler FEBRABAN
        b.Numeric(18, 6, totals.LoteCount);               // qtd de lotes
        b.Numeric(24, 6, totals.TotalRecordCount);        // qtd de registros do arquivo
        b.Blank(30, 6);                                   // qtd contas conciliação (Santander: blank)
        b.Blank(36, 205);                                 // filler FEBRABAN

        return b.BuildComplete();
    }

    // ---- Return outcome map ----------------------------------------------------------------

    /// <summary>
    /// Occurrence codes treated as a successful settlement — mirrors the legado (orcamento) success
    /// whitelist so this conciliation flags the same titles as "paid" the operators already know.
    /// Codes carry the Santander table text: <c>00</c> "Crédito ou Débito Efetivado", <c>03</c> "Débito
    /// Autorizado pela Agência - Efetuado", <c>BD</c>/<c>BE</c>/<c>BF</c> "Inclusão/Alteração/Exclusão
    /// Efetuada com Sucesso". A blank occurrence is also Paid (see <see cref="OutcomeFor"/>).
    /// ✅ <c>00</c> confirmed against a real .RET (FORN, 15/07/2026): 117/120 payments.
    /// <c>EB</c> stays OUT — not in the FEBRABAN/Santander table (<see cref="CnabReturnCodes"/>), likely a
    /// transposition of <c>BE</c>. Unlike the legado, an UNKNOWN code is never auto-rejected — it falls to
    /// <see cref="ReturnOutcome.Unknown"/> (manual review), so an unmapped settlement code never releases a
    /// paid installment. 🔶 <c>BD</c>/<c>BE</c>/<c>BF</c> not yet seen in a real payment .RET; the
    /// exact-value guard (<c>ActualValue == Amount</c>) protects against a wrong baixa.
    /// </summary>
    private static readonly IReadOnlySet<string> SantanderPaidCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "00", // FEBRABAN: no occurrence / accepted with no errors
        "03", // Santander: "Débito Autorizado pela Agência - Efetuado" — settlement
        "BD", // Santander: "Inclusão Efetuada com Sucesso"
        "BE", // Santander: "Alteração Efetuada com Sucesso" (legado parity — AEL-918)
        "BF", // Santander: "Exclusão Efetuada com Sucesso" (legado parity — AEL-918)
    };

    /// <summary>
    /// Occurrence codes that indicate the payment was rejected. Descriptions match
    /// <see cref="CnabReturnCodes"/> (the full occurrence table, AEL-797) — keep the two in step.
    /// <list type="bullet">
    ///   <item><c>01</c> — "Insuficiência de Fundos - Débito Não Efetuado".</item>
    ///   <item><c>02</c> — "Crédito ou Débito Cancelado pelo Pagador/Credor".</item>
    ///   <item><c>AG</c> — "Agência/Conta Corrente/DV Inválido".</item>
    ///   <item><c>AB</c> — "Tipo de Operação Inválido".</item>
    ///   <item><c>AC</c> — "Tipo de Serviço Inválido".</item>
    ///   <item><c>PA</c> — "Pix não efetivado" (seen 1× in the real .RET of 15/07/2026).</item>
    ///   <item><c>PF</c> — "CPF/CNPJ do usuário recebedor não é consistente com o titular da conta"
    ///     (seen 2× in the same file).</item>
    /// </list>
    /// <para>
    /// <b>AEL-797 — removed from this set:</b> <c>03</c>, <c>05</c>, <c>06</c>, <c>09</c>, <c>10</c>.
    /// They were a best-effort guess that the occurrence table contradicts: <c>03</c> is
    /// "Débito Autorizado pela Agência - <b>Efetuado</b>" (a settlement, not a rejection) and the
    /// others don't exist in the payment-return table (they belong to the cobrança table). Rejecting a
    /// settled payment releases the installment back to the queue and risks paying it twice.
    /// </para>
    /// AEL-913: <c>03</c> is now in <see cref="SantanderPaidCodes"/> (settlement); the others
    /// (<c>05</c>/<c>06</c>/<c>09</c>/<c>10</c>) remain out of both sets → <see cref="ReturnOutcome.Unknown"/>
    /// (manual review), the fail-safe default.
    /// </summary>
    private static readonly IReadOnlySet<string> SantanderRejectedCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "01", // Insuficiência de Fundos - Débito Não Efetuado
        "02", // Crédito ou Débito Cancelado pelo Pagador/Credor
        "AG", // Agência/Conta Corrente/DV Inválido
        "AB", // Tipo de Operação Inválido
        "AC", // Tipo de Serviço Inválido
        "PA", // Pix não efetivado
        "PF", // CPF/CNPJ do recebedor inconsistente com o titular da conta
    };

    /// <inheritdoc/>
    public Application.Remittances.ReturnOutcome OutcomeFor(IReadOnlyList<string> occurrenceCodes)
    {
        // A reject code anywhere in the list takes precedence (bank may combine codes).
        foreach (var code in occurrenceCodes)
        {
            if (SantanderRejectedCodes.Contains(code))
                return Application.Remittances.ReturnOutcome.Rejected;
        }

        // Hybrid (AEL-918): any code that is not a known settlement code → Unknown (manual review) —
        // never auto-reject an unmapped code (that could release a paid installment). If every present
        // code is a known paid code — or the occurrence field is blank — the payment settled.
        foreach (var code in occurrenceCodes)
        {
            if (!SantanderPaidCodes.Contains(code))
                return Application.Remittances.ReturnOutcome.Unknown;
        }

        return Application.Remittances.ReturnOutcome.Paid;
    }

    // ---- Helpers ----------------------------------------------------------------------------

    /// <summary>
    /// Santander convênio composition for the 20-byte field (Header de Arquivo/Lote pos 33-52):
    /// <c>BBBBAAAACCCCCCCCCCCC</c> = <c>0033</c> + agência(4, no DV) + convênio(12 digits).
    /// </summary>
    private string AgreementField(RemittancePayerInfo payer)
    {
        var bank = BankCode.PadLeft(4, '0');                  // 0033
        var ag = OnlyDigits(payer.Agency);
        ag = ag.Length >= 4 ? ag[^4..] : ag.PadLeft(4, '0');
        var agreement = OnlyDigits(payer.AgreementCode).PadLeft(12, '0');
        if (agreement.Length > 12)
            agreement = agreement[^12..];
        return bank + ag + agreement;
    }

    /// <summary>Favorecido inscription type: 1 = CPF (11 digits), 2 = CNPJ (14 digits), 0 = exempt/unknown.</summary>
    private static string FavoredInscriptionType(string document)
    {
        var digits = OnlyDigits(document);
        return digits.Length switch
        {
            11 => "1",
            14 => "2",
            _ => "0",
        };
    }

    /// <summary>
    /// PIX "Forma de Iniciação" (Segmento B 15-16) inferred from the key shape:
    /// 02 = email, 03 = CPF/CNPJ, 01 = phone (+55…), 04 = random EVP (UUID). Defaults to 04.
    /// </summary>
    private static string PixInitiationForm(RemittanceFileLine line)
    {
        var key = line.FavoredPixKey?.Trim() ?? string.Empty;
        if (key.Contains('@'))
            return "02"; // email
        var digits = OnlyDigits(key);
        if (key.StartsWith('+') || digits.Length is 12 or 13)
            return "01"; // phone (+55DDDNNNNNNNN)
        if (digits.Length is 11 or 14 && digits.Length == key.Length)
            return "03"; // CPF / CNPJ
        return "04";     // random EVP (UUID) / other
    }

    private static string OnlyDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return new string(value.Where(char.IsAsciiDigit).ToArray());
    }
}
