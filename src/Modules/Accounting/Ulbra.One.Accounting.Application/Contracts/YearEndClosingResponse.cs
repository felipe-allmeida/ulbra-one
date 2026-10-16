using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>Result of a year-end closing run.</summary>
[SwaggerSchema("Outcome of a year-end close (apuração do resultado). The operation is idempotent — "
    + "running it twice does not post the result twice, it reports that the year was already closed.")]
public sealed record YearEndClosingResponse(
    [property: SwaggerSchema("The fiscal year that was closed.")]
    [property: SwaggerExample("2026")]
    int FiscalYear,
    [property: SwaggerSchema("Whether the year was already closed before this call. True means nothing "
        + "was posted now and the figures below describe the earlier close.")]
    bool AlreadyClosed,
    [property: SwaggerSchema("Result carried to retained earnings, in BRL — positive for profit.")]
    [property: SwaggerExample("183160.00")]
    decimal NetIncome,
    [property: SwaggerSchema("How many result accounts were zeroed out. Revenue and expense accounts "
        + "start the next year at zero; balance-sheet accounts carry their balances over.")]
    [property: SwaggerExample("148")]
    int ResultAccountsCleared)
{
    public static YearEndClosingResponse From(int fiscalYear, YearEndClosingResult r) =>
        new(fiscalYear, r.AlreadyClosed, r.NetIncome, r.ResultAccountsCleared);
}
