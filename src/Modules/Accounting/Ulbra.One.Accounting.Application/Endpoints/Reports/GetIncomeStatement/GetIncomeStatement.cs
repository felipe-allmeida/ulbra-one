using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetIncomeStatement;

/// <summary>DRE for a competence range. <c>from</c>/<c>to</c> default to the full history up to today.</summary>
public sealed class GetIncomeStatement : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/income-statement", Handle).WithName("GetIncomeStatement").WithTags("Accounting")
            .RequirePermission(Permissions.AccountingRead).Produces<IncomeStatementResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, TimeProvider clock, CompanyId? companyId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rangeFrom = from ?? DateOnly.MinValue;
        var rangeTo = to ?? today;
        if (rangeTo < rangeFrom)
            return TypedResults.Problem(detail: "'to' must not precede 'from'.", statusCode: 400, title: "IncomeStatement.InvalidRange");

        var lines = await PostedLinesLoader.LoadAsync(uow, companyId?.Value, ct);
        var dre = IncomeStatementCalculator.Build(lines, rangeFrom, rangeTo);
        return TypedResults.Ok(IncomeStatementResponse.Of(dre, companyId));
    }
}
