using System.Text;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// Assembles the CNAB240 remittance file from a <see cref="RemittanceFileInput"/> using an
/// <see cref="ICnabBankProfile"/> for the byte layout. Pure — returns the file bytes, no I/O.
///
/// Structure: Header de Arquivo (0) → for each payment-method group a lote
/// [Header de Lote (1) → per line (Segmento A (3) + Segmento B (3)) → Trailer de Lote (5)]
/// → Trailer de Arquivo (9). Records are joined with CRLF and encoded Latin-1 (ISO-8859-1),
/// with no trailing record after the type-9 trailer.
/// </summary>
public sealed class Cnab240RemittanceWriter : IRemittanceFileGenerator
{
    private const string RecordTerminator = "\r\n";

    private readonly ICnabBankProfile _profile;

    public Cnab240RemittanceWriter(ICnabBankProfile profile)
    {
        _profile = profile;
    }

    public byte[] Generate(RemittanceFileInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var records = new List<string> { _profile.BuildFileHeader(input) };

        // Group lines into separate lotes by forma de lançamento (Header de Lote carries one forma).
        var batches = GroupIntoBatches(input.Lines);

        foreach (var batch in batches)
        {
            records.Add(_profile.BuildBatchHeader(input, batch));

            // numero_sequencial_registro_lote increments per detail record (A=1, B=2, A=3, B=4 …).
            var sequenceInBatch = 0;
            foreach (var line in batch.Lines)
            {
                records.Add(_profile.BuildSegmentA(input, batch, line, ++sequenceInBatch));
                records.Add(_profile.BuildSegmentB(input, batch, line, ++sequenceInBatch));
            }

            records.Add(_profile.BuildBatchTrailer(batch));
        }

        var totals = new CnabFileTotals(LoteCount: batches.Count, TotalRecordCount: records.Count + 1);
        records.Add(_profile.BuildFileTrailer(input, totals));

        return Encode(records);
    }

    /// <summary>
    /// Splits the lines into method-keyed lotes (1-based lote numbers in first-seen order), so TED and
    /// PIX lines land in distinct lotes — each Header de Lote carries a single forma de lançamento.
    /// </summary>
    private static IReadOnlyList<CnabBatchContext> GroupIntoBatches(IReadOnlyList<RemittanceFileLine> lines)
    {
        var order = new List<PaymentMethod>();
        var byMethod = new Dictionary<PaymentMethod, List<RemittanceFileLine>>();

        foreach (var line in lines)
        {
            if (!byMethod.TryGetValue(line.Method, out var bucket))
            {
                bucket = new List<RemittanceFileLine>();
                byMethod[line.Method] = bucket;
                order.Add(line.Method);
            }

            bucket.Add(line);
        }

        var batches = new List<CnabBatchContext>(order.Count);
        var loteNumber = 0;
        foreach (var method in order)
            batches.Add(new CnabBatchContext(++loteNumber, method, byMethod[method]));

        return batches;
    }

    private static byte[] Encode(IReadOnlyList<string> records)
    {
        var latin1 = Encoding.Latin1;
        var sb = new StringBuilder();
        foreach (var record in records)
        {
            if (record.Length != 240)
                throw new InvalidOperationException($"CNAB record must be exactly 240 chars, got {record.Length}.");
            sb.Append(record).Append(RecordTerminator);
        }

        return latin1.GetBytes(sb.ToString());
    }
}
