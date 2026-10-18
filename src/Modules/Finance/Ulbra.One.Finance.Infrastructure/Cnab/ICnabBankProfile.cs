using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// A bank-specific CNAB240 layout: knows the COMPE code, how to route a payment line into a lote
/// category, which detail segments each category expands a line into, and how to render each
/// record. The writer (<see cref="Cnab240RemittanceWriter"/>) owns the file/lote structure and
/// sequence counters; the profile owns the byte layout of each record.
/// Implementors also implement <see cref="IReturnOutcomeResolver"/> for use in Application-layer services.
/// </summary>
public interface ICnabBankProfile : IReturnOutcomeResolver
{
    /// <summary>COMPE code written into position 1-3 of every record (e.g. <c>033</c>).</summary>
    string BankCode { get; }

    /// <summary>Human-readable bank name written into the file header.</summary>
    string BankName { get; }

    /// <summary>
    /// Maps a payment line to its lote category (drives grouping + segment set + Header de Lote
    /// forma/serviço/versão). A single lote carries exactly one category.
    /// </summary>
    CnabLoteCategory CategoryFor(RemittanceFileLine line);

    /// <summary>The ordered detail segments each line of a category expands into (e.g. [A, B]).</summary>
    IReadOnlyList<CnabSegment> DetailSegmentsFor(CnabLoteCategory category);

    string BuildFileHeader(RemittanceFileInput input);

    string BuildBatchHeader(RemittanceFileInput input, CnabBatchContext batch);

    /// <summary>Renders one detail (tipo 3) segment for a line, dispatching on <paramref name="segment"/>.</summary>
    string BuildDetailSegment(
        RemittanceFileInput input,
        CnabBatchContext batch,
        RemittanceFileLine line,
        CnabSegment segment,
        int sequenceInBatch);

    /// <summary>
    /// Renders the Trailer de Lote. <paramref name="recordCountInLote"/> is the writer-computed
    /// total (header + detail records + trailer) — detail count varies per category
    /// (A+B = 2/line, J+J-52 = 2/line, O = 1/line), so the writer supplies it.
    /// </summary>
    string BuildBatchTrailer(CnabBatchContext batch, int recordCountInLote);

    string BuildFileTrailer(RemittanceFileInput input, CnabFileTotals totals);
}

/// <summary>
/// The category of a CNAB240 lote — determines the Header de Lote forma/serviço/versão and the
/// detail segments emitted per line. Boleto splits into <see cref="BoletoBancario"/> vs
/// <see cref="Convenio"/> by the código de barras (convênio starts with <c>8</c>).
/// </summary>
public enum CnabLoteCategory
{
    Pix,
    Ted,
    BoletoBancario,
    Convenio,
    /// <summary>PIX QR Code dinâmico (copia e cola): forma de lançamento 47, Segmentos J-PIX + J52-PIX.</summary>
    PixQrCode,
}

/// <summary>A CNAB240 detail (tipo 3) segment identifier.</summary>
public enum CnabSegment
{
    A,
    B,
    J,
    J52,
    O,
    /// <summary>Segmento J do PIX QR (forma 47) — layout distinto do J de boleto (TXID/valores, sem código de barras).</summary>
    JPix,
    /// <summary>Segmento J52-PIX (forma 47) — pagador + recebedor + URL de endereçamento; distinto do J52 de boleto.</summary>
    J52Pix,
}

/// <summary>Per-lote context: the category it carries, its 1-based lote number, and its lines.</summary>
public sealed record CnabBatchContext(
    int LoteNumber,
    CnabLoteCategory Category,
    IReadOnlyList<RemittanceFileLine> Lines)
{
    /// <summary>Σ of the lote's payment amounts (Trailer de Lote "Somatória dos Valores").</summary>
    public decimal TotalAmount
    {
        get
        {
            var sum = 0m;
            foreach (var line in Lines)
                sum += line.Amount;
            return sum;
        }
    }
}

/// <summary>File-level totals for the Trailer de Arquivo.</summary>
public sealed record CnabFileTotals(int LoteCount, int TotalRecordCount);
