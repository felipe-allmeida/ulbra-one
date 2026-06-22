using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetTrialBalance;

public sealed class GetTrialBalance : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/trial-balance", Handle).WithName("GetTrialBalance").WithTags("Accounting")
            .RequireAuthorization().Produces<TrialBalanceResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(IUnitOfWork uow, CompanyId? companyId, CancellationToken ct)
    {
        var lines = await PostedLinesLoader.LoadAsync(uow, companyId?.Value, ct);
        var trialBalance = TrialBalanceCalculator.Build(lines);
        return TypedResults.Ok(TrialBalanceResponse.From(trialBalance, companyId));
    }
}
