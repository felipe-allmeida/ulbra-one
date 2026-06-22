using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetBalanceSheet;

/// <summary>Balance sheet (Balanço Patrimonial) as of a date (defaults to today).</summary>
public sealed class GetBalanceSheet : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/balance-sheet", Handle).WithName("GetBalanceSheet").WithTags("Accounting")
            .RequireAuthorization().Produces<BalanceSheetResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, TimeProvider clock, CompanyId? companyId, DateOnly? asOf, CancellationToken ct)
    {
        var cutoff = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var lines = await PostedLinesLoader.LoadAsync(uow, companyId?.Value, ct);
        var balanceSheet = BalanceSheetCalculator.Build(lines, cutoff);
        return TypedResults.Ok(BalanceSheetResponse.From(balanceSheet, companyId));
    }
}
