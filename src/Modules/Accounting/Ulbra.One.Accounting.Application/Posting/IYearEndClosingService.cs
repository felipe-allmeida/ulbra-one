using Ulbra.One.Common.Domain;

namespace Ulbra.One.Accounting.Application.Posting;

/// <summary>Outcome of a year-end closing run.</summary>
/// <param name="AlreadyClosed">True when the exercise had already been closed (idempotent no-op).</param>
/// <param name="NetIncome">Lucro/Prejuízo líquido do exercício apurado (transferido ao PL); positive = lucro.</param>
/// <param name="ResultAccountsCleared">How many result accounts were zeroed into Apuração.</param>
public sealed record YearEndClosingResult(bool AlreadyClosed, decimal NetIncome, int ResultAccountsCleared);

/// <summary>Runs the annual closing (encerramento anual) for a company/exercise: zeroes the result accounts
/// into the transitory "Apuração do Resultado do Exercício" account, transfers the net result to Lucros/Prejuízos
/// Acumulados, and closes the fiscal year's periods. Idempotent per (company, fiscal year).</summary>
public interface IYearEndClosingService
{
    Task<Result<YearEndClosingResult>> CloseYearAsync(Guid companyId, int fiscalYear, CancellationToken ct);
}
