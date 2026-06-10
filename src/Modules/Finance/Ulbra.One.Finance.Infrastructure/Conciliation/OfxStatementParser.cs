using OfxSharp;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.Conciliation;

/// <summary>
/// Parses OFX (SGML/XML) bank statements into normalized <see cref="ParsedStatement"/> records
/// using jcoliz.OfxSharp.NetStandard, keeping the OFX library out of the Application layer.
/// </summary>
public sealed class OfxStatementParser : IBankStatementParser
{
    public StatementSource Source => StatementSource.OfxUpload;

    public async Task<ParsedStatement> ParseAsync(Stream content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var document = await OfxDocumentReader.FromSgmlFileAsync(content).ConfigureAwait(false);

        var transactions = new List<ParsedTransaction>();
        DateOnly? periodStart = null;
        DateOnly? periodEnd = null;

        foreach (var statement in document.Statements)
        {
            if (statement is null)
            {
                continue;
            }

            periodStart ??= DateOnly.FromDateTime(statement.TransactionsStart.DateTime);
            periodEnd ??= DateOnly.FromDateTime(statement.TransactionsEnd.DateTime);

            foreach (var tx in statement.Transactions)
            {
                if (tx is null)
                {
                    continue;
                }

                var date = tx.Date ?? default;
                var description = !string.IsNullOrWhiteSpace(tx.Memo)
                    ? tx.Memo
                    : tx.Name ?? string.Empty;

                transactions.Add(new ParsedTransaction(
                    ExternalId: tx.TransactionId ?? string.Empty,
                    Date: DateOnly.FromDateTime(date.DateTime),
                    Amount: tx.Amount,
                    Description: description));
            }
        }

        return new ParsedStatement(periodStart, periodEnd, transactions);
    }
}
