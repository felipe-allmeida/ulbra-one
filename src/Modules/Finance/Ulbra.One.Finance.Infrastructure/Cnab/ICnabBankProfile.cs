using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// A bank-specific CNAB240 layout: knows the COMPE code, how to map our
/// <see cref="PaymentMethod"/> to a "forma de lançamento", and how to render each of the
/// six record types. The writer (<see cref="Cnab240RemittanceWriter"/>) owns the file/lote
/// structure and sequence counters; the profile owns the byte layout of each record.
/// </summary>
public interface ICnabBankProfile
{
    /// <summary>COMPE code written into position 1-3 of every record (e.g. <c>033</c>).</summary>
    string BankCode { get; }

    /// <summary>Human-readable bank name written into the file header.</summary>
    string BankName { get; }

    /// <summary>The "forma de lançamento" (Header de Lote pos 12-13) for a given payment method.</summary>
    string FormaLancamentoFor(PaymentMethod method);

    string BuildFileHeader(RemittanceFileInput input);

    string BuildBatchHeader(RemittanceFileInput input, CnabBatchContext batch);

    string BuildSegmentA(RemittanceFileInput input, CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch);

    string BuildSegmentB(RemittanceFileInput input, CnabBatchContext batch, RemittanceFileLine line, int sequenceInBatch);

    string BuildBatchTrailer(CnabBatchContext batch);

    string BuildFileTrailer(RemittanceFileInput input, CnabFileTotals totals);
}

/// <summary>Per-lote context: the method group it carries, its 1-based lote number, and its lines.</summary>
public sealed record CnabBatchContext(
    int LoteNumber,
    PaymentMethod Method,
    IReadOnlyList<RemittanceFileLine> Lines)
{
    /// <summary>Records in this lote = Header + Trailer + (Segmento A + Segmento B) per line.</summary>
    public int RecordCount => 2 + (2 * Lines.Count);

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
