using System.Text;
using Ulbra.One.Finance.Application.Remittances;

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

        // Group lines into separate lotes by category (Header de Lote carries one forma/serviço/versão).
        var batches = GroupIntoBatches(input.Lines);

        foreach (var batch in batches)
        {
            records.Add(_profile.BuildBatchHeader(input, batch));

            // numero_sequencial_registro_lote increments per detail record. The segment set per line
            // is category-driven: [A,B] for PIX/TED, [J,J-52] for boleto bancário, [O] for convênio.
            var segments = _profile.DetailSegmentsFor(batch.Category);
            var sequenceInBatch = 0;
            foreach (var line in batch.Lines)
                foreach (var segment in segments)
                    records.Add(_profile.BuildDetailSegment(input, batch, line, segment, ++sequenceInBatch));

            // Trailer de Lote count = header + detail records + trailer.
            records.Add(_profile.BuildBatchTrailer(batch, 2 + sequenceInBatch));
        }

        var totals = new CnabFileTotals(LoteCount: batches.Count, TotalRecordCount: records.Count + 1);
        records.Add(_profile.BuildFileTrailer(input, totals));

        return Encode(records);
    }

    /// <summary>
    /// Splits the lines into category-keyed lotes (1-based lote numbers in first-seen order), so PIX,
    /// TED, boleto bancário and convênio lines land in distinct lotes — each Header de Lote carries a
    /// single forma/serviço/versão, and boleto bancário and convênio never share a lote.
    /// </summary>
    private IReadOnlyList<CnabBatchContext> GroupIntoBatches(IReadOnlyList<RemittanceFileLine> lines)
    {
        var order = new List<CnabLoteCategory>();
        var byCategory = new Dictionary<CnabLoteCategory, List<RemittanceFileLine>>();

        foreach (var line in lines)
        {
            var category = _profile.CategoryFor(line);
            if (!byCategory.TryGetValue(category, out var bucket))
            {
                bucket = new List<RemittanceFileLine>();
                byCategory[category] = bucket;
                order.Add(category);
            }

            bucket.Add(line);
        }

        var batches = new List<CnabBatchContext>(order.Count);
        var loteNumber = 0;
        foreach (var category in order)
            batches.Add(new CnabBatchContext(++loteNumber, category, byCategory[category]));

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
