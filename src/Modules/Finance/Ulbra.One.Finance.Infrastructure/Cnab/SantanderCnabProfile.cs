using System.Globalization;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// Banco Santander (033) "Pagamento a Fornecedores" CNAB240 layout — YLEC_2403 v11.3.1,
/// over the FEBRABAN v10.11 base. Field positions/lengths are encoded inline below; every
/// record is verified to cover all 240 bytes via <see cref="CnabRecordBuilder.BuildComplete"/>.
///
/// See <c>.superpowers/sdd/cnab240-layout.md</c> for the authoritative field map and the
/// homologação flags (TED forma de lançamento code, PIX Segmento B sub-boundaries).
/// </summary>
public sealed class SantanderCnabProfile : ICnabBankProfile
{
    public string BankCode => "033";
    public string BankName => "BANCO SANTANDER";

    /// <summary>Tipo de Serviço (Header de Lote 10-11): 20 = Pagamento a Fornecedores.</summary>
    private const string TipoServicoFornecedores = "20";

    /// <summary>Forma de Lançamento — crédito em conta (not used: our PaymentMethod resolves to Pix/Ted only).</summary>
    private const string FormaCreditoEmConta = "01";

    /// <summary>
    /// Forma de Lançamento — TED. Santander v11.3.1 G002 lists <c>03</c> = DOC/TED; the FEBRABAN-standard
    /// value is <c>41</c> (TED Outra Titularidade). We emit Santander's <c>03</c>;
    /// CONFIRM in homologação (see cnab240-layout.md "Forma de Lançamento — TED" flag).
    /// </summary>
    public const string FormaTed = "03";

    /// <summary>Forma de Lançamento — PIX Transferência.</summary>
    public const string FormaPix = "45";

    /// <summary>Versão do layout do arquivo (Header de Arquivo 164-166) — Santander fixed value.</summary>
    private const string FileLayoutVersion = "060";

    /// <summary>Versão do layout do lote (Header de Lote 14-16) — Santander fixed value.</summary>
    private const string BatchLayoutVersion = "031";

    /// <summary>Câmara centralizadora for TED (STR/CIP) — Segmento A 18-20.</summary>
    private const string CamaraTed = "018";

    /// <summary>BACEN finalidade da TED — required when câmara = 018. 00001 = crédito em conta (generic).</summary>
    private const string TedPurposeCode = "00001";

    public string FormaLancamentoFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Ted => FormaTed,
        PaymentMethod.Pix => FormaPix,
        _ => throw new NotSupportedException(
            $"Payment method '{method}' is not supported for CNAB240 remittance (only Pix and Ted).")
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
        b.Blank(58, 1);                                  // dv_agencia (optional / blank)
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
        b.Numeric(167, 5, "0");                          // densidade_gravacao = 00000
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
        b.Numeric(10, 2, TipoServicoFornecedores);        // tipo_servico = 20
        b.Numeric(12, 2, FormaLancamentoFor(batch.Method)); // forma_lancamento
        b.Numeric(14, 3, BatchLayoutVersion);             // versao_layout_lote
        b.Blank(17, 1);                                   // filler FEBRABAN
        b.Numeric(18, 1, "2");                            // tipo_inscricao_empresa = 2 (CNPJ)
        b.Numeric(19, 14, p.Cnpj);                        // numero_inscricao_empresa
        b.Alpha(33, 20, AgreementField(p));               // codigo_convenio
        b.Numeric(53, 5, p.Agency);                       // agencia_mantenedora
        b.Blank(58, 1);                                   // dv_agencia
        b.Numeric(59, 12, p.AccountNumber);               // numero_conta_corrente
        b.Alpha(71, 1, p.AccountDigit);                   // dv_conta
        b.Blank(72, 1);                                   // dv_agencia_conta
        b.Alpha(73, 30, p.LegalName, allowTruncate: true);// nome_empresa
        b.Blank(103, 40);                                 // informacao_1 (mensagem)
        b.Blank(143, 30);                                 // endereco_logradouro
        b.Numeric(173, 5, "0");                           // endereco_numero
        b.Blank(178, 15);                                 // endereco_complemento
        b.Blank(193, 20);                                 // endereco_cidade
        b.Numeric(213, 5, "0");                           // endereco_cep
        b.Numeric(218, 3, "0");                           // endereco_cep_complemento
        b.Blank(221, 2);                                  // endereco_uf
        b.Blank(223, 8);                                  // filler FEBRABAN
        b.Blank(231, 10);                                 // ocorrencias_retorno

        return b.BuildComplete();
    }

    // ---- Record 3: Segmento A ---------------------------------------------------------------

    public string BuildSegmentA(RemittanceFileInput input, CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch)
    {
        var isTed = batch.Method == PaymentMethod.Ted;
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
            b.Numeric(18, 3, CamaraTed);                  // codigo_camara = 018
            b.Numeric(21, 3, line.FavoredBankCode);       // codigo_banco_favorecido
            b.Numeric(24, 5, line.FavoredAgency);         // agencia_favorecido
            b.Blank(29, 1);                               // dv_agencia (favorecido)
            b.Numeric(30, 12, line.FavoredAccount);       // conta_favorecido
            b.Alpha(42, 1, line.FavoredAccountDigit);     // dv_conta (favorecido)
            b.Blank(43, 1);                               // dv_agencia_conta
        }
        else
        {
            b.Numeric(18, 3, "0");                        // codigo_camara = 000 (PIX: not via câmara)
            b.Numeric(21, 3, "0");                        // codigo_banco_favorecido (blank/zeros)
            b.Numeric(24, 5, "0");                        // agencia_favorecido
            b.Blank(29, 1);                               // dv_agencia
            b.Numeric(30, 12, "0");                       // conta_favorecido
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
        b.Alpha(220, 5, isTed ? TedPurposeCode : null);   // codigo_finalidade_ted (only for TED)
        b.Blank(225, 2);                                  // complemento_finalidade
        b.Blank(227, 3);                                  // exclusivo_febraban
        b.Numeric(230, 1, "0");                           // aviso_favorecido = 0
        b.Blank(231, 10);                                 // ocorrencias_retorno

        return b.BuildComplete();
    }

    // ---- Record 3: Segmento B ---------------------------------------------------------------

    public string BuildSegmentB(RemittanceFileInput input, CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch)
    {
        var isPix = batch.Method == PaymentMethod.Pix;
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
        b.Blank(33, 35);                                  // Informação 10 (TXID — blank for key transfer)
        b.Blank(68, 60);                                  // Informação 11 (mensagem livre)
        b.Alpha(128, 99, isPix ? line.FavoredPixKey : null, allowTruncate: true); // Informação 12 — CHAVE PIX (128-226)
        b.Blank(227, 6);                                  // uso_reservado_banco
        b.Blank(233, 8);                                  // ISPB (blank/zeros if N/A)

        return b.BuildComplete();
    }

    // ---- Record 5: Trailer de Lote ----------------------------------------------------------

    public string BuildBatchTrailer(CnabBatchContext batch)
    {
        var b = new CnabRecordBuilder(240);

        b.Numeric(1, 3, BankCode);                        // codigo_banco
        b.Numeric(4, 4, batch.LoteNumber);                // lote_servico
        b.Numeric(8, 1, "5");                             // tipo_registro = 5
        b.Blank(9, 9);                                    // filler FEBRABAN
        b.Numeric(18, 6, batch.RecordCount);              // qtd registros do lote (header+trailer+2/line)
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
    /// Occurrence codes that indicate the payment was successfully settled by Santander.
    /// <list type="bullet">
    ///   <item><c>00</c> — FEBRABAN sentinel: "sem ocorrência" / crédito efetuado (accepted with no errors).</item>
    ///   <item><c>BD</c> — Santander: "Pagamento Efetuado" (payment settled — most common success code in retorno).</item>
    ///   <item><c>EB</c> — Santander: "Agendamento Confirmado" (scheduled, treated as eventual-paid at this stage).</item>
    /// </list>
    /// 🔶 Homologação flag: confirm these codes against a real Santander .RET file in homologação
    /// before using <see cref="ReturnOutcome.Paid"/> for automated accounting entries.
    /// </summary>
    private static readonly IReadOnlySet<string> SantanderPaidCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "00", // FEBRABAN: sem ocorrência / aceito sem erros
        "BD", // Santander: pagamento efetuado
        "EB", // Santander: agendamento confirmado
    };

    /// <summary>
    /// Occurrence codes that indicate the payment was rejected by Santander.
    /// <list type="bullet">
    ///   <item><c>AG</c> — Santander: "Não Efetuado" (payment not made — generic bank rejection).</item>
    ///   <item><c>02</c> — FEBRABAN/Santander: "Débito não efetuado — insuficiência de fundos".</item>
    ///   <item><c>03</c> — FEBRABAN: "Banco inválido".</item>
    ///   <item><c>05</c> — FEBRABAN: "Agência inválida".</item>
    ///   <item><c>06</c> — FEBRABAN: "Número da conta inválido/encerrada".</item>
    ///   <item><c>09</c> — FEBRABAN: "Dados bancários inconsistentes".</item>
    ///   <item><c>10</c> — FEBRABAN: "Produto de empresa bloqueado".</item>
    ///   <item><c>AB</c> — Santander: "Rejeitado — erro de favorecido".</item>
    ///   <item><c>AC</c> — Santander: "Rejeitado — dado do favorecido inválido".</item>
    /// </list>
    /// 🔶 Homologação flag: this set is best-effort from the FEBRABAN v10.11 table and
    /// Santander YLEC_2403 v11.3.1. Validate against live .RET files in homologação.
    /// </summary>
    private static readonly IReadOnlySet<string> SantanderRejectedCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "AG", // Santander: não efetuado (generic bank rejection)
        "02", // FEBRABAN: débito não efetuado — insuficiência de fundos
        "03", // FEBRABAN: banco inválido
        "05", // FEBRABAN: agência inválida
        "06", // FEBRABAN: número da conta inválido / encerrada
        "09", // FEBRABAN: dados bancários inconsistentes
        "10", // FEBRABAN: produto bloqueado
        "AB", // Santander: rejeitado — erro de favorecido
        "AC", // Santander: rejeitado — dado do favorecido inválido
    };

    /// <inheritdoc/>
    public ReturnOutcome OutcomeFor(IReadOnlyList<string> occurrenceCodes)
    {
        // A reject code anywhere in the list takes precedence (bank may combine codes).
        foreach (var code in occurrenceCodes)
        {
            if (SantanderRejectedCodes.Contains(code))
                return ReturnOutcome.Rejected;
        }

        // If every non-blank code is in the paid set → Paid.
        if (occurrenceCodes.Count > 0)
        {
            foreach (var code in occurrenceCodes)
            {
                if (SantanderPaidCodes.Contains(code))
                    return ReturnOutcome.Paid;
            }
        }

        return ReturnOutcome.Unknown;
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

    /// <summary>Favorecido inscription type: 1 = CPF (11 digits), 2 = CNPJ (14 digits), 0 = isento/unknown.</summary>
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
