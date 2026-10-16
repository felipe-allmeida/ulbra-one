using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// Synthesises a minimal CNAB240 return (.RET) file that marks all Pending lines in a remittance
/// as paid (occurrence code "BD" — Santander: Pagamento Efetuado).
///
/// Used exclusively for Development-mode simulation via the <c>simulate-return</c> endpoint.
/// Not registered in production (the endpoint gate ensures it is unreachable in non-Dev environments).
/// </summary>
public sealed class CnabReturnSimulator : IRemittanceReturnSimulator
{
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public CnabReturnSimulator(IUnitOfWork uow, TimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<(byte[] Bytes, string FileName)?> SimulateAsync(
        Guid remittanceId, DateOnly today, CancellationToken ct)
    {
        var remittance = await _uow.ReadSet<Remittance>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == remittanceId, ct);

        if (remittance is null)
            return null;

        var pendingLines = remittance.Lines
            .Where(l => l.Status == RemittanceLineStatus.Pending)
            .ToList();

        if (pendingLines.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.Append(BuildFileHeader(remittance.SequenceNumber)); sb.Append("\r\n");
        sb.Append(BuildBatchHeader()); sb.Append("\r\n");

        var seq = 1;
        foreach (var line in pendingLines)
        {
            sb.Append(BuildSegmentA(line.Reference, today, line.Amount, seq)); sb.Append("\r\n");
            sb.Append(BuildSegmentB(seq + 1)); sb.Append("\r\n");
            seq += 2;
        }

        var totalRecordsInBatch = 2 + (2 * pendingLines.Count); // header + trailer + (A+B per line)
        sb.Append(BuildBatchTrailer(pendingLines.Count, pendingLines.Sum(l => l.Amount))); sb.Append("\r\n");
        sb.Append(BuildFileTrailer(loteCount: 1, totalRecordCount: totalRecordsInBatch + 2)); sb.Append("\r\n"); // +2 for file header+trailer

        var bytes = Encoding.Latin1.GetBytes(sb.ToString());
        var fileName = $"retorno-{remittance.SequenceNumber:D6}.ret";
        return (bytes, fileName);
    }

    // ---- Record 0: File Header ---------------------------------------------------------

    private static string BuildFileHeader(int sequenceNumber)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "0");
        b.Numeric(8, 1, "0");
        b.Blank(9, 9);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");                       // numero_inscricao placeholder
        b.Alpha(33, 20, "0033000000000000");          // codigo_convenio placeholder
        b.Numeric(53, 5, "0");
        b.Blank(58, 1);
        b.Numeric(59, 12, "0");
        b.Alpha(71, 1, "0");
        b.Blank(72, 1);
        b.Alpha(73, 30, "SIMULACAO RETORNO");
        b.Alpha(103, 30, "BANCO SANTANDER");
        b.Blank(133, 10);
        b.Numeric(143, 1, "2");                       // codigo_remessa = 2 (retorno)
        b.Numeric(144, 8, "00000000");                // data_geracao placeholder
        b.Numeric(152, 6, "000000");                  // hora_geracao placeholder
        b.Numeric(158, 6, sequenceNumber);
        b.Numeric(164, 3, "060");
        b.Numeric(167, 5, "0");
        b.Blank(172, 20);
        b.Blank(192, 20);
        b.Blank(212, 19);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    // ---- Record 1: Batch Header --------------------------------------------------------

    private static string BuildBatchHeader()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "1");
        b.Alpha(9, 1, "C");
        b.Numeric(10, 2, "20");
        b.Numeric(12, 2, "03");
        b.Numeric(14, 3, "031");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Alpha(33, 20, "0033000000000000");
        b.Numeric(53, 5, "0");
        b.Blank(58, 1);
        b.Numeric(59, 12, "0");
        b.Alpha(71, 1, "0");
        b.Blank(72, 1);
        b.Alpha(73, 30, "SIMULACAO RETORNO");
        b.Blank(103, 40);
        b.Blank(143, 30);
        b.Numeric(173, 5, "0");
        b.Blank(178, 15);
        b.Blank(193, 20);
        b.Numeric(213, 5, "0");
        b.Numeric(218, 3, "0");
        b.Blank(221, 2);
        b.Blank(223, 8);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    // ---- Record 3: Segmento-A ----------------------------------------------------------

    /// <summary>
    /// Builds a Segmento-A detail record for a paid line:
    /// Reference in "Seu Número" (pos 74-93), actual date (pos 155-162) and actual value (pos 163-177)
    /// populated, occurrence code "BD" (Santander: Pagamento Efetuado) at pos 231-240.
    /// </summary>
    private static string BuildSegmentA(string reference, DateOnly actualDate, decimal actualValue, int seq)
    {
        // Pad reference to exactly 20 chars (left-justified, right-padded with spaces).
        var refPadded = reference.Length < 20 ? reference.PadRight(20) : reference[..20];

        // "BD        " = "BD" + 8 spaces (10-char occurrence field, only first 2 filled).
        const string occurrences = "BD        ";

        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, seq);
        b.Alpha(14, 1, "A");
        b.Numeric(15, 1, "0");
        b.Numeric(16, 2, "0");
        b.Numeric(18, 3, "18");
        b.Numeric(21, 3, "0");
        b.Numeric(24, 5, "0");
        b.Blank(29, 1);
        b.Numeric(30, 12, "0");
        b.Blank(42, 1);
        b.Blank(43, 1);
        b.Alpha(44, 30, "SIMULACAO", allowTruncate: true);
        b.Alpha(74, 20, refPadded, allowTruncate: true);
        b.Date(94, 8, actualDate);
        b.Alpha(102, 3, "BRL");
        b.Numeric(105, 15, "0");
        b.Money(120, 15, actualValue);
        b.Blank(135, 20);              // Nosso Número (blank — simulated)
        b.Date(155, 8, actualDate);    // data_real_efetivacao
        b.Money(163, 15, actualValue); // valor_real_efetivacao = same as requested (paid in full)
        b.Blank(178, 40);
        b.Blank(218, 2);
        b.Alpha(220, 5, "00001");
        b.Blank(225, 2);
        b.Blank(227, 3);
        b.Numeric(230, 1, "0");
        b.Alpha(231, 10, occurrences, allowTruncate: true);
        return b.BuildComplete();
    }

    // ---- Record 3: Segmento-B (minimal placeholder) ------------------------------------

    private static string BuildSegmentB(int seq)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, seq);
        b.Alpha(14, 1, "B");
        b.Numeric(15, 2, "0");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Blank(33, 35);
        b.Blank(68, 60);
        b.Blank(128, 99);
        b.Blank(227, 6);
        b.Blank(233, 8);
        return b.BuildComplete();
    }

    // ---- Record 5: Batch Trailer -------------------------------------------------------

    private static string BuildBatchTrailer(int lineCount, decimal totalAmount)
    {
        var recordCount = 2 + (2 * lineCount); // batch header + trailer + (A+B per line)
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "5");
        b.Blank(9, 9);
        b.Numeric(18, 6, recordCount);
        b.Money(24, 18, totalAmount);
        b.Numeric(42, 18, "0");
        b.Numeric(60, 6, "0");
        b.Blank(66, 165);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    // ---- Record 9: File Trailer --------------------------------------------------------

    private static string BuildFileTrailer(int loteCount, int totalRecordCount)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "9999");
        b.Numeric(8, 1, "9");
        b.Blank(9, 9);
        b.Numeric(18, 6, loteCount);
        b.Numeric(24, 6, totalRecordCount);
        b.Blank(30, 6);
        b.Blank(36, 205);
        return b.BuildComplete();
    }
}
