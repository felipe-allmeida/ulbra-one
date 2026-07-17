using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.Conciliation;

/// <summary>
/// Parses OFX 1.x (SGML) bank statements — as exported directly by Brazilian banks such as Santander —
/// into normalized <see cref="ParsedStatement"/> records. Owned in-house (no third-party OFX library) so we
/// control the two things that break real Santander files: pt-BR decimal formatting (comma decimal, dot
/// thousands, see <see cref="OfxAmount"/>) and the <c>CHARSET</c>/<c>ENCODING</c> header (see
/// <see cref="OfxEncoding"/>). Tolerant of unclosed SGML leaf tags. Kept in Infrastructure so no OFX-specific
/// parsing leaks into the Application layer. Pure/stateless — safe to register as a singleton.
/// </summary>
public sealed class OfxStatementParser : IBankStatementParser
{
    public StatementSource Source => StatementSource.OfxUpload;

    public async Task<ParsedStatement> ParseAsync(Stream content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct).ConfigureAwait(false);

        var text = OfxEncoding.Decode(buffer.ToArray());
        return OfxSgmlReader.Read(text);
    }
}

/// <summary>Decodes OFX bytes honoring the header <c>CHARSET</c>/<c>ENCODING</c> and a UTF-8 BOM.</summary>
internal static class OfxEncoding
{
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // The header region is always ASCII; Latin1 decodes it 1:1 without throwing.
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 2048));

        var encoding = HeaderValue(head, "ENCODING");
        var charset = HeaderValue(head, "CHARSET");

        // ENCODING:UNICODE / CHARSET UTF-8 → UTF-8. Everything else (USASCII + CHARSET:1252/8859-1/NONE)
        // → Latin1: a superset of ASCII that covers the accented characters Brazilian banks emit, and the
        // convention already used by the CNAB240 parser. Avoids depending on CodePagesEncodingProvider.
        var enc = IsUtf8(encoding) || IsUtf8(charset) ? new UTF8Encoding(false) : Encoding.Latin1;
        return enc.GetString(bytes);
    }

    private static bool IsUtf8(string? value) =>
        value is not null &&
        (value.Equals("UNICODE", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("UTF", StringComparison.OrdinalIgnoreCase));

    private static string? HeaderValue(string head, string key)
    {
        var m = Regex.Match(head, key + @":\s*([^\r\n]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}

/// <summary>
/// Minimal, tolerant OFX 1.x SGML walker. Reads only the bank-statement subset Santander exports
/// (<c>BANKACCTFROM</c>, <c>BANKTRANLIST</c> period, and each <c>STMTTRN</c>). Treats the text after an
/// opening leaf tag, up to the next <c>&lt;</c>, as that tag's value — inherently tolerant of the unclosed
/// leaf tags SGML uses.
/// </summary>
internal static class OfxSgmlReader
{
    // Matches an opening/closing tag and the (possibly empty) text following it up to the next '<'.
    private static readonly Regex TagRegex =
        new(@"<(/?)([A-Za-z0-9.]+)>([^<]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ParsedStatement Read(string text)
    {
        var transactions = new List<ParsedTransaction>();

        string? bankId = null, acctId = null, acctType = null;
        string? dtStart = null, dtEnd = null;
        var inAccount = false;

        var inTxn = false;
        string? txDate = null, txAmount = null, txFitId = null, txMemo = null, txName = null;

        void FlushTransaction()
        {
            if (!inTxn)
                return;

            var description = !string.IsNullOrWhiteSpace(txMemo) ? txMemo!.Trim() : txName?.Trim() ?? string.Empty;
            transactions.Add(new ParsedTransaction(
                ExternalId: txFitId?.Trim() ?? string.Empty,
                Date: OfxDate.ParseDate(txDate) ?? default,
                Amount: OfxAmount.Parse(txAmount),
                Description: WebUtility.HtmlDecode(description)));

            inTxn = false;
            txDate = txAmount = txFitId = txMemo = txName = null;
        }

        foreach (Match m in TagRegex.Matches(text))
        {
            var closing = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToUpperInvariant();
            var value = m.Groups[3].Value;

            switch (tag)
            {
                case "BANKACCTFROM":
                    inAccount = !closing;
                    continue;
                case "STMTTRN":
                    FlushTransaction();
                    inTxn = !closing;
                    continue;
            }

            if (closing)
                continue;

            if (inAccount)
            {
                switch (tag)
                {
                    case "BANKID": bankId = value.Trim(); break;
                    case "ACCTID": acctId = value.Trim(); break;
                    case "ACCTTYPE": acctType = value.Trim(); break;
                }

                continue;
            }

            if (inTxn)
            {
                switch (tag)
                {
                    case "DTPOSTED": txDate = value; break;
                    case "TRNAMT": txAmount = value; break;
                    case "FITID": txFitId = value; break;
                    case "MEMO": txMemo = value; break;
                    case "NAME": txName = value; break;
                }

                continue;
            }

            switch (tag)
            {
                case "DTSTART": dtStart ??= value; break;
                case "DTEND": dtEnd ??= value; break;
            }
        }

        FlushTransaction();

        DateOnly? periodStart = OfxDate.ParseDate(dtStart);
        DateOnly? periodEnd = OfxDate.ParseDate(dtEnd);
        if (transactions.Count > 0)
        {
            periodStart ??= transactions.Min(t => t.Date);
            periodEnd ??= transactions.Max(t => t.Date);
        }

        var account = bankId is null && acctId is null && acctType is null
            ? null
            : new ParsedAccount(bankId, acctId, acctType);

        return new ParsedStatement(periodStart, periodEnd, transactions, account);
    }
}
