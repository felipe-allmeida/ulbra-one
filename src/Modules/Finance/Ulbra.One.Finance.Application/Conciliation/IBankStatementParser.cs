using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Conciliation;

public sealed record ParsedTransaction(string ExternalId, DateOnly Date, decimal Amount, string Description);

/// <summary>Bank account block read from the OFX <c>BANKACCTFROM</c> (bank code, account id, account type).</summary>
public sealed record ParsedAccount(string? BankId, string? AccountId, string? AccountType);

public sealed record ParsedStatement(DateOnly? PeriodStart, DateOnly? PeriodEnd, IReadOnlyList<ParsedTransaction> Transactions, ParsedAccount? Account);

public interface IBankStatementParser
{
    StatementSource Source { get; }
    Task<ParsedStatement> ParseAsync(Stream content, CancellationToken ct);
}
