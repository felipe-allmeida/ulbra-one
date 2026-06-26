using System.Text;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// Parses a CNAB240 return (.RET) file into <see cref="CnabReturnEntry"/> instances by reading
/// only the Segmento-A detalhe records (tipo_registro == '3', codigo_segmento == 'A').
/// All other record types (file/lote headers, Segmento B, trailers) are ignored.
///
/// Pure: no I/O, no state — safe to register as a singleton.
/// See <c>.superpowers/sdd/cnab240-layout.md</c> for field positions.
/// </summary>
public sealed class Cnab240ReturnParser : ICnabReturnParser
{
    // Segmento A layout constants (1-based, per cnab240-layout.md).
    private const int PosRegistroType = 8;     // length 1 — '3' = detalhe
    private const int PosSegmento = 14;        // length 1 — 'A'
    private const int PosReference = 74;       // length 20 — "Seu Número"
    private const int PosBankRef = 135;        // length 20 — "Nosso Número"
    private const int PosActualDate = 155;     // length 8  — DDMMAAAA
    private const int PosActualValue = 163;    // length 15 — 2 implied decimals
    private const int PosOccurrences = 231;    // length 10 — up to 5×2-char codes

    private const int RecordLength = 240;
    private const int OccurrenceFieldLength = 10;
    private const int OccurrenceCodeLength = 2;

    /// <inheritdoc/>
    public IReadOnlyList<CnabReturnEntry> Parse(byte[] file)
    {
        // Decode Latin-1 (ISO-8859-1) — CNAB240 encoding per spec.
        var text = Encoding.Latin1.GetString(file);

        // Split on CRLF or bare LF; empty entries come from the trailing terminator.
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var entries = new List<CnabReturnEntry>();

        foreach (var line in lines)
        {
            // Skip records that are not exactly 240 chars (defensive — should not occur in valid files).
            if (line.Length != RecordLength)
                continue;

            // Only Segmento-A detalhe records contain payment return data.
            if (line[PosRegistroType - 1] != '3')
                continue;
            if (line[PosSegmento - 1] != 'A')
                continue;

            var reader = new CnabRecordReader(line);

            var reference = reader.Alpha(PosReference, 20);
            var bankRef = reader.Alpha(PosBankRef, 20);
            var actualDate = reader.Date(PosActualDate, 8);
            var actualValue = reader.Money(PosActualValue, 15);
            var occurrenceCodes = ParseOccurrenceCodes(reader.Alpha(PosOccurrences, OccurrenceFieldLength));

            entries.Add(new CnabReturnEntry(reference, occurrenceCodes, actualValue, actualDate, bankRef));
        }

        return entries.AsReadOnly();
    }

    /// <summary>
    /// Split the 10-character occurrence field into up-to-5 two-character codes.
    /// Drops blank ("  ") and all-zero ("00") codes — <c>"00"</c> is the FEBRABAN "no occurrence"
    /// sentinel (accepted with no errors), which is meaningful to <see cref="ReturnOutcome"/> but
    /// is not a distinct actionable code by itself; keep it so callers can inspect it but mark blank
    /// filler as noise.
    ///
    /// Rule: drop only pure-whitespace pairs; keep "00" because it is the success sentinel.
    /// </summary>
    private static IReadOnlyList<string> ParseOccurrenceCodes(string occurrenceField)
    {
        var codes = new List<string>(OccurrenceFieldLength / OccurrenceCodeLength);
        var raw = occurrenceField.PadRight(OccurrenceFieldLength); // guard if Alpha trimmed trailing spaces

        for (var i = 0; i < OccurrenceFieldLength; i += OccurrenceCodeLength)
        {
            var code = raw.Substring(i, OccurrenceCodeLength);
            if (string.IsNullOrWhiteSpace(code))
                continue; // blank padding — skip
            codes.Add(code);
        }

        return codes.AsReadOnly();
    }
}
