using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Conciliation;

public sealed record ParsedTransaction(string ExternalId, DateOnly Date, decimal Amount, string Description);
public sealed record ParsedStatement(DateOnly? PeriodStart, DateOnly? PeriodEnd, IReadOnlyList<ParsedTransaction> Transactions);

public interface IBankStatementParser
{
    StatementSource Source { get; }
    Task<ParsedStatement> ParseAsync(Stream content, CancellationToken ct);
}
