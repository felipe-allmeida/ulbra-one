using System.Text;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// Parses a CNAB240 return (.RET) file into <see cref="CnabReturnEntry"/> instances. Reads the
/// payment detalhe segments — <b>A</b> (PIX/TED), <b>J</b> (boleto bancário) and <b>O</b>
/// (convênio/tributo) — and attaches the authentication code from a following Segmento <b>Z</b>.
/// File/lote headers, Segmento B and trailers are skipped.
///
/// Pure: no I/O, no state — safe to register as a singleton.
/// See <c>docs/cnab240-layout.md</c> for field positions.
///
/// The Seg J-52 (sacado/cedente companion) shares the 'J' segment code but is identified by "52" at
/// positions 18-19 and carries no settlement — it is skipped, never read as a Seg J payment.
///
/// ✅ Confirmed byte-exact against a real Santander .RET (FORN, 15/07/2026, 120 pagamentos): Seg A
/// "Seu Número" (74), "Nosso Número" (135), data real (155), valor real (163) and ocorrência (231);
/// Seg B favored document (19-32, subtypes 03/05); Seg Z authentication (15-78); batch trailer total
/// (24-41). Summed details reconciled with the trailer to the cent (R$ 931.926,95).
/// 🔶 Not yet validated against a real file: the Seg J (boleto) and Seg O (convênio) return positions —
/// that file only had PIX/TED (Seg A). The J-52 skip is spec-derived, pending a real boleto retorno.
/// </summary>
public sealed class Cnab240ReturnParser : ICnabReturnParser
{
    private const int PosLote = 4;             // length 4 — lote de serviço
    private const int LoteLength = 4;
    private const int PosRegistroType = 8;     // length 1 — '3' = detalhe, '5' = trailer de lote
    private const int PosSegmento = 14;        // length 1 — segment code (A/B/J/O/Z)
    private const int PosOccurrences = 231;    // length 10 — up to 5×2-char codes (all segments)
    private const int PosTrailerTotal = 24;    // length 18 — Trailer de Lote "Somatória dos Valores"
    private const int TrailerTotalLength = 18;

    // Per-segment field positions (1-based): Reference ("Seu Número"), BankReference ("Nosso Número"),
    // ActualDate (data efetivação/pagamento), ActualValue (valor efetivado/pago), BarcodePos (código de
    // barras, 44 pos — Seg J/O only, used as a fallback match key).
    // NamePos = favored/cedente name (A 44-73; J/O 62-91). LaunchPos = "Valor do Lançamento" (A 120-134;
    // J 100-114; O 108-122) — read by the crítica flow, where the real value (ValuePos) is zero.
    private static readonly SegmentLayout SegA = new(RefPos: 74, BankRefPos: 135, DatePos: 155, ValuePos: 163, ValueLen: 15, NamePos: 44, LaunchPos: 120);
    private static readonly SegmentLayout SegJ = new(RefPos: 183, BankRefPos: 203, DatePos: 145, ValuePos: 153, ValueLen: 15, BarcodePos: 18, NamePos: 62, LaunchPos: 100);
    private static readonly SegmentLayout SegO = new(RefPos: 123, BankRefPos: 143, DatePos: 100, ValuePos: 108, ValueLen: 15, BarcodePos: 18, NamePos: 62, LaunchPos: 108);

    private const int BarcodeLength = 44;

    // Segmento Z — authentication code (positions 15-78, 64 chars).
    private const int PosAuthCode = 15;
    private const int AuthCodeLength = 64;

    // Segmento B — favored CPF/CNPJ (positions 19-32), companion to a Seg A.
    private const int PosFavoredDocument = 19;
    private const int FavoredDocumentLength = 14;

    // Segmento B — favored PIX key (Informação 12, positions 128-226), companion to a Seg A PIX entry.
    private const int PosFavoredPixKey = 128;
    private const int PixKeyLength = 99;

    // Segmento J-52 shares the 'J' segment code (position 14) with the Seg J boleto payment but is a
    // sacado/cedente companion carrying no settlement — it is identified by "52" at positions 18-19
    // (FEBRABAN). It must NOT be parsed as a Seg J payment (would yield a spurious entry from the wrong
    // positions and double-count the value in CheckTrailers).
    private const int PosJ52Marker = 18;
    private const string SegmentJ52Marker = "52";
    private const int SegmentMarkerLength = 2;

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
        var lastPaymentIndex = -1; // index in `entries` of the last A/J/O, for attaching a following Seg Z

        foreach (var line in lines)
        {
            // Skip records that are not exactly 240 chars (defensive — should not occur in valid files).
            if (line.Length != RecordLength)
                continue;

            // Only detalhe records (tipo 3) carry payment data / segments.
            if (line[PosRegistroType - 1] != '3')
                continue;

            var segment = line[PosSegmento - 1];

            // Seg J-52 companion (no settlement) — skip so it isn't read as a Seg J payment.
            if (IsSegmentJ52(line))
                continue;

            var reader = new CnabRecordReader(line);

            var layout = LayoutFor(segment);

            if (layout is not null)
            {
                var occurrences = ParseOccurrenceCodes(reader.Alpha(PosOccurrences, OccurrenceFieldLength));
                entries.Add(new CnabReturnEntry(
                    Reference: reader.Alpha(layout.RefPos, 20),
                    OccurrenceCodes: occurrences,
                    ActualValue: reader.Money(layout.ValuePos, layout.ValueLen),
                    ActualDate: reader.Date(layout.DatePos, 8),
                    BankReference: reader.Alpha(layout.BankRefPos, 20),
                    Barcode: layout.BarcodePos is int bp ? reader.Alpha(bp, BarcodeLength) : null,
                    FavoredName: reader.Alpha(layout.NamePos, 30),
                    LaunchValue: reader.Money(layout.LaunchPos, layout.ValueLen)));
                lastPaymentIndex = entries.Count - 1;
                continue;
            }

            // Segmento Z: authentication code belongs to the preceding A/J/O record.
            if (segment == 'Z' && lastPaymentIndex >= 0 && entries[lastPaymentIndex].AuthCode is null)
            {
                var auth = reader.Alpha(PosAuthCode, AuthCodeLength);
                if (!string.IsNullOrWhiteSpace(auth))
                    entries[lastPaymentIndex] = entries[lastPaymentIndex] with { AuthCode = auth };
                continue;
            }

            // Segmento B: favored CPF/CNPJ + PIX key belong to the preceding Seg A record (fallback keys).
            if (segment == 'B' && lastPaymentIndex >= 0)
            {
                var e = entries[lastPaymentIndex];
                if (e.FavoredDocument is null)
                {
                    var doc = reader.Alpha(PosFavoredDocument, FavoredDocumentLength);
                    if (!string.IsNullOrWhiteSpace(doc) && doc.TrimStart('0').Length > 0)
                        e = e with { FavoredDocument = doc };
                }
                if (e.FavoredPixKey is null)
                {
                    var pixKey = reader.Alpha(PosFavoredPixKey, PixKeyLength);
                    if (!string.IsNullOrWhiteSpace(pixKey))
                        e = e with { FavoredPixKey = pixKey };
                }
                entries[lastPaymentIndex] = e;
            }

            // Any other detail segment carries no return data here — skipped.
        }

        return entries.AsReadOnly();
    }

    /// <inheritdoc/>
    public IReadOnlyList<CnabTrailerCheck> CheckTrailers(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var actualByLote = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var declaredByLote = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var line in lines)
        {
            if (line.Length != RecordLength)
                continue;

            var lote = line.Substring(PosLote - 1, LoteLength);
            var recordType = line[PosRegistroType - 1];
            var reader = new CnabRecordReader(line);

            if (recordType == '3')
            {
                if (IsSegmentJ52(line))
                    continue; // Seg J-52 companion — no settled value (would double-count the Seg J)
                var layout = LayoutFor(line[PosSegmento - 1]);
                if (layout is null)
                    continue; // Segmento B/Z etc. carry no settled value
                if (!actualByLote.ContainsKey(lote))
                {
                    actualByLote[lote] = 0m;
                    order.Add(lote);
                }
                actualByLote[lote] += reader.Money(layout.ValuePos, layout.ValueLen);
            }
            else if (recordType == '5')
            {
                declaredByLote[lote] = reader.Money(PosTrailerTotal, TrailerTotalLength);
            }
        }

        var checks = new List<CnabTrailerCheck>();
        foreach (var lote in order)
        {
            if (declaredByLote.TryGetValue(lote, out var declared))
                checks.Add(new CnabTrailerCheck(lote, declared, actualByLote[lote]));
        }

        return checks.AsReadOnly();
    }

    // File Header (tipo 0) payer identity — for a paying-account cross-check (positions 1-based).
    private const int PosPayerDocument = 19;
    private const int PayerDocumentLength = 14;
    private const int PosPayerAgency = 53;
    private const int PayerAgencyLength = 5;
    private const int PosPayerAgencyDigit = 58;
    private const int PosPayerAccount = 59;
    private const int PayerAccountLength = 12;
    private const char FileHeaderType = '0';

    /// <inheritdoc/>
    public CnabReturnHeader? ParseHeader(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Length != RecordLength)
                continue;
            if (line[PosRegistroType - 1] != FileHeaderType) // File Header (tipo 0)
                continue;

            var reader = new CnabRecordReader(line);
            return new CnabReturnHeader(
                PayerDocument: reader.Alpha(PosPayerDocument, PayerDocumentLength),
                Agency: reader.Alpha(PosPayerAgency, PayerAgencyLength),
                AgencyDigit: reader.Alpha(PosPayerAgencyDigit, 1),
                Account: reader.Alpha(PosPayerAccount, PayerAccountLength));
        }

        return null;
    }

    private static SegmentLayout? LayoutFor(char segment) => segment switch
    {
        'A' => SegA,
        'J' => SegJ,
        'O' => SegO,
        _ => null,
    };

    /// <summary>True for a Segmento J-52 (sacado/cedente companion) — segment 'J' with "52" at 18-19.
    /// Shares the 'J' code with the boleto payment but carries no settlement, so it must be skipped.</summary>
    private static bool IsSegmentJ52(string line) =>
        line[PosSegmento - 1] == 'J'
        && line.Substring(PosJ52Marker - 1, SegmentMarkerLength) == SegmentJ52Marker;

    /// <summary>
    /// Split the 10-character occurrence field into up-to-5 two-character codes.
    /// Drops blank ("  ") padding; keeps "00" (FEBRABAN "no occurrence" success sentinel).
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

    /// <summary>Per-segment field positions for the return record (1-based). BarcodePos is null for
    /// Seg A (no código de barras); Seg J/O carry it at position 18.</summary>
    private sealed record SegmentLayout(int RefPos, int BankRefPos, int DatePos, int ValuePos, int ValueLen, int? BarcodePos = null, int NamePos = 0, int LaunchPos = 0);
}
